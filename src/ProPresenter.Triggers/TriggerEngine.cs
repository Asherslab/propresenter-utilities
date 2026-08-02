using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProPresenter.Api;
using ProPresenter.Api.Actions;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers;

/// <summary>A timer's latest tick, with everything the UI needs to render a row.</summary>
public sealed record TimerSnapshot(
    ProId Id,
    TimerConfiguration? Configuration,
    TimerObservation Observation,
    DateTimeOffset At);

/// <summary>
/// Watches the timer stream and fires the active profile's rules.
/// </summary>
public sealed class TriggerEngine : IDisposable
{
    private readonly TimerStream _stream;
    private readonly ProPresenterClient _client;
    private readonly ActionExecutor _executor;
    private readonly ConfigurationStore _config;
    private readonly ActivityLog _activity;
    private readonly ILogger<TriggerEngine> _logger;

    private readonly ConcurrentDictionary<string, TimerRuntime> _runtime = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TimerConfiguration> _configurations = new(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyList<TimerSnapshot> _snapshot = [];
    private ConnectionState? _lastConnectionState;

    public TriggerEngine(
        TimerStream stream,
        ProPresenterClient client,
        ActionExecutor executor,
        ConfigurationStore config,
        ActivityLog activity,
        ILogger<TriggerEngine> logger)
    {
        _stream = stream;
        _client = client;
        _executor = executor;
        _config = config;
        _activity = activity;
        _logger = logger;

        _stream.TimersUpdated += OnTimersUpdated;
        _stream.StatusChanged += OnStatusChanged;
    }

    /// <summary>Latest tick for every timer ProPresenter reported.</summary>
    public IReadOnlyList<TimerSnapshot> Timers => _snapshot;

    /// <summary>Raised after each tick, so the UI can re-render.</summary>
    public event Action? Updated;

    public ConnectionStatus ConnectionStatus => _stream.Status;

    /// <summary>Re-reads timer definitions. Called on connect and whenever timers are edited.</summary>
    public async Task RefreshTimerConfigurationsAsync(CancellationToken ct = default)
    {
        var timers = await _client.GetTimersAsync(ct);
        if (timers.Count == 0 && _configurations.IsEmpty) return;

        _configurations.Clear();
        foreach (var timer in timers)
        {
            var key = KeyFor(timer.Id);
            if (key is not null) _configurations[key] = timer;
        }

        Updated?.Invoke();
    }

    /// <summary>
    /// Whether a status change represents a genuine (re)connection. Guards against a repeated
    /// <see cref="ConnectionState.Connected"/> notification, which would otherwise discard every
    /// timer's baseline and silently stop all cues from firing.
    /// </summary>
    internal static bool IsNewConnection(ConnectionState? previous, ConnectionState current) =>
        current == ConnectionState.Connected && previous != ConnectionState.Connected;

    private void OnStatusChanged(ConnectionStatus status)
    {
        var previous = _lastConnectionState;
        _lastConnectionState = status.State;

        if (IsNewConnection(previous, status.State))
        {
            _activity.Success($"Connected to ProPresenter at {_client.Endpoint.BaseUri.Authority}");

            // Definitions may have changed while we were away, and every timer needs a fresh
            // baseline so nothing that already passed fires late.
            _runtime.Clear();
            _ = Task.Run(() => RefreshTimerConfigurationsAsync(CancellationToken.None));
        }
        else if (status.State == ConnectionState.Disconnected
            && previous == ConnectionState.Connected
            && status.LastError is not null)
        {
            _activity.Warning("Lost connection to ProPresenter", status.LastError);
        }

        Updated?.Invoke();
    }

    private void OnTimersUpdated(IReadOnlyList<TimerCurrent> timers)
    {
        var configuration = _config.Current;
        var rules = configuration.TriggersArmed
            ? configuration.ActiveProfile?.Rules ?? []
            : [];

        var now = DateTimeOffset.Now;
        var snapshots = new List<TimerSnapshot>(timers.Count);
        var toFire = new List<TriggerRule>();

        foreach (var timer in timers)
        {
            var key = KeyFor(timer.Id);
            if (key is null) continue;

            var runtime = _runtime.GetOrAdd(key, _ => new TimerRuntime());
            _configurations.TryGetValue(key, out var definition);

            // Count-down-to-time timers have no configured length, so measure elapsed ourselves from
            // the moment we first saw them running.
            if (timer.State is TimerState.Running or TimerState.Overrunning)
            {
                runtime.RunningSince ??= now;
            }
            else
            {
                runtime.RunningSince = null;
            }

            var trackedElapsed = runtime.RunningSince is { } since ? now - since : (TimeSpan?)null;
            var observation = TimerObservation.Create(definition, timer, trackedElapsed);

            var applicable = rules
                .Where(rule => rule.Source == TriggerSource.Timer && rule.Timer.Matches(timer.Id))
                .ToList();

            var result = TriggerEvaluator.Evaluate(runtime.Previous, observation, applicable);

            if (result.Disposition == TickDisposition.ReBaselined && applicable.Count > 0)
            {
                _logger.LogInformation("Timer '{Timer}' jumped backwards; re-arming its triggers", timer.Id);
            }

            runtime.Previous = observation;
            toFire.AddRange(result.Fired);

            snapshots.Add(new TimerSnapshot(timer.Id, definition, observation, now));
        }

        _snapshot = snapshots;

        if (toFire.Count > 0)
        {
            // Dispatch off the stream thread: an unreachable ProPresenter must not stall tick handling.
            _ = Task.Run(() => FireAsync(toFire, CancellationToken.None));
        }

        Updated?.Invoke();
    }

    private async Task FireAsync(IReadOnlyList<TriggerRule> rules, CancellationToken ct)
    {
        foreach (var rule in rules)
        {
            _activity.Info($"Trigger fired: {rule.DisplayName}", $"{rule.Timer.Display} — {rule.ConditionDescription}");
            await RunActionsAsync(rule.Actions, rule.DisplayName, ct);
        }
    }

    /// <summary>Runs a rule's actions in order, reporting each outcome to the activity log.</summary>
    public async Task RunActionsAsync(IReadOnlyList<ActionInvocation> actions, string context, CancellationToken ct = default)
    {
        if (actions.Count == 0)
        {
            _activity.Warning($"{context} has no actions configured");
            return;
        }

        foreach (var action in actions)
        {
            ActionOutcome outcome;
            try
            {
                outcome = await _executor.ExecuteAsync(action, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Action {Action} threw", action.ActionId);
                _activity.Error($"{context}: {ActionExecutor.Describe(action)} threw", ex.Message);
                continue;
            }

            if (outcome.Success)
            {
                _activity.Success($"{context}: {outcome.Summary}");
            }
            else
            {
                _activity.Error($"{context}: {outcome.Summary} failed", outcome.Error);
            }
        }
    }

    /// <summary>Runs a rule's actions immediately, regardless of its condition or the master switch.</summary>
    public Task TestFireAsync(TriggerRule rule, CancellationToken ct = default) =>
        RunActionsAsync(rule.Actions, $"Test: {rule.DisplayName}", ct);

    /// <summary>
    /// Runs every armed rule listening for <paramref name="signalKey"/>. This is the macro path: a
    /// ProPresenter macro sends a RossTalk command (or something calls the webhook), and the rules
    /// attached to that signal fire through exactly the same action pipeline as timer rules.
    /// </summary>
    /// <returns>How many rules matched, so the caller can report unrecognised signals.</returns>
    public int HandleSignal(string signalKey, string origin)
    {
        var configuration = _config.Current;

        var matched = configuration.ActiveProfile?.Rules
            .Where(rule => rule.Enabled && rule.MatchesSignal(signalKey))
            .ToList() ?? [];

        if (!configuration.TriggersArmed)
        {
            if (matched.Count > 0)
            {
                _activity.Warning(
                    $"Signal {signalKey} ignored — triggers are disarmed",
                    $"{matched.Count} rule(s) would have fired");
            }

            return 0;
        }

        if (matched.Count == 0) return 0;

        // Dispatch off the caller's thread: the RossTalk listener must stay responsive, and a rule
        // may carry a delay.
        _ = Task.Run(() => FireSignalRulesAsync(matched, signalKey, origin, CancellationToken.None));
        return matched.Count;
    }

    private async Task FireSignalRulesAsync(
        IReadOnlyList<TriggerRule> rules,
        string signalKey,
        string origin,
        CancellationToken ct)
    {
        // Rules with a delay run concurrently with the rest, so one waiting cue does not hold up
        // another that should fire immediately.
        await Task.WhenAll(rules.Select(async rule =>
        {
            if (rule.Offset > TimeSpan.Zero)
            {
                _activity.Info(
                    $"Signal {signalKey} → {rule.DisplayName} in {TimeCode.Format(rule.Offset)}",
                    origin);

                try
                {
                    await Task.Delay(rule.Offset, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            _activity.Info($"Trigger fired: {rule.DisplayName}", $"{signalKey} — {origin}");
            await RunActionsAsync(rule.Actions, rule.DisplayName, ct);
        }));
    }

    private static string? KeyFor(ProId id) =>
        !string.IsNullOrWhiteSpace(id.Uuid) ? id.Uuid :
        !string.IsNullOrWhiteSpace(id.Name) ? "name:" + id.Name :
        null;

    public void Dispose()
    {
        _stream.TimersUpdated -= OnTimersUpdated;
        _stream.StatusChanged -= OnStatusChanged;
    }

    private sealed class TimerRuntime
    {
        public TimerObservation? Previous { get; set; }

        /// <summary>When this process first observed the timer counting, for count-down-to-time elapsed.</summary>
        public DateTimeOffset? RunningSince { get; set; }
    }
}

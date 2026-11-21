using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ProPresenter.Api;
using ProPresenter.Api.Actions;
using ProPresenter.Api.Shelly;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers.Lights;

/// <summary>What the dashboard shows about the FOH lights.</summary>
public sealed record LightsStatus(
    LatchPhase Phase,
    string? VideoName,
    bool? LastCommandedOn,
    DateTimeOffset? LastChange,
    bool RestorePending);

/// <summary>
/// Runs the <c>lights.*</c> actions against the Shelly relay, and gives the lights back when a video
/// that turned them off ends.
/// </summary>
/// <remarks>
/// Only <c>lights.off_for_video</c> arms the restore. Lights turned off any other way — the countdown
/// cue, a manual button — stay off until something turns them on, which is what keeps an excluded
/// video or the walk-in countdown from bringing them up.
/// </remarks>
public sealed class LightsController(
    ShellyClient shelly,
    ProPresenterClient propresenter,
    ConfigurationStore config,
    ActivityLog activity,
    ILogger<LightsController> logger) : IActionHandler
{
    /// <summary>The transport layer that media-layer video reports on in ProPresenter's API.</summary>
    public const string VideoLayer = "presentation";

    /// <summary>
    /// How long an ended video waits before the lights come up, unless the action says otherwise. A
    /// video cut straight into another lights-off video re-arms within this window, so the room does
    /// not flash. Kept short because it is felt in the room: the next cue's macro arrives over
    /// RossTalk within milliseconds.
    /// </summary>
    internal static readonly TimeSpan DefaultRestoreDelay = TimeSpan.FromSeconds(0.3);

    private static readonly TimeSpan MaxRestoreDelay = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly VideoLatch _latch = new();

    private IReadOnlyList<int> _latchChannels = [];
    private bool _latchOrdered = true;
    private TimeSpan _latchRestoreDelay = DefaultRestoreDelay;
    private TimeSpan _failsafeMargin;
    private CancellationTokenSource? _pendingRestore;
    private bool? _lastCommandedOn;
    private DateTimeOffset? _lastChange;

    public ActionTarget Target => ActionTarget.Lights;

    /// <summary>Raised whenever the latch or the commanded state changes.</summary>
    public event Action? Updated;

    /// <summary>True while a video is being waited on, so the watcher knows to poll.</summary>
    public bool IsWatching
    {
        get { lock (_gate) return _latch.Phase != LatchPhase.Idle; }
    }

    public LightsStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new LightsStatus(_latch.Phase, _latch.Video?.Name, _lastCommandedOn, _lastChange,
                    _pendingRestore is not null);
            }
        }
    }

    public async Task<ActionOutcome> ExecuteAsync(ActionDefinition definition, ActionInvocation invocation, CancellationToken ct)
    {
        var summary = ActionExecutor.Describe(definition, invocation);
        var settings = config.Current.Lights;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host))
        {
            return new ActionOutcome(false, summary, "FOH lights are not set up — see Settings → FOH lights.");
        }

        IReadOnlyList<int> channels;
        TimeSpan failsafe;
        var ordered = !string.Equals(invocation.Arguments.GetValueOrDefault("order"), "all together",
            StringComparison.OrdinalIgnoreCase);
        try
        {
            channels = ParseChannels(invocation.Arguments.GetValueOrDefault("channels"), settings.Channels);
            failsafe = ParseSeconds(invocation.Arguments.GetValueOrDefault("failsafe"));
        }
        catch (FormatException ex)
        {
            return new ActionOutcome(false, summary, ex.Message);
        }

        return definition.Id switch
        {
            "lights.set" => await SetAsync(
                string.Equals(invocation.Arguments.GetValueOrDefault("state"), "on", StringComparison.OrdinalIgnoreCase),
                channels, failsafe, summary, ordered, ct),
            "lights.off_for_video" => await OffForVideoAsync(channels, failsafe,
                ParseDuration(invocation.Arguments.GetValueOrDefault("restore_delay"), DefaultRestoreDelay, MaxRestoreDelay),
                summary, ordered, ct),
            _ => new ActionOutcome(false, summary, $"Unknown lights action '{definition.Id}'."),
        };
    }

    /// <summary>The dashboard buttons. Cancels any pending video restore, like any other explicit switch.</summary>
    public async Task<ActionOutcome> SetManuallyAsync(bool on, CancellationToken ct = default)
    {
        var settings = config.Current.Lights;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host))
        {
            return new ActionOutcome(false, "Lights", "FOH lights are not set up.");
        }

        ActionOutcome outcome;
        try
        {
            outcome = await SetAsync(on, ParseChannels("all", settings.Channels), TimeSpan.Zero,
                on ? "Manual: lights on" : "Manual: lights off", ordered: true, ct);
        }
        catch (FormatException ex)
        {
            outcome = new ActionOutcome(false, "Lights", ex.Message);
        }

        if (outcome.Success) activity.Success(outcome.Summary);
        else activity.Error($"{outcome.Summary} failed", outcome.Error);
        return outcome;
    }

    public async Task<IReadOnlyDictionary<int, bool?>> ReadStatesAsync(CancellationToken ct = default)
    {
        var settings = config.Current.Lights;
        return await shelly.GetStatesAsync(settings.Host, settings.Password,
            ParseChannels("all", settings.Channels), ct);
    }

    private async Task<ActionOutcome> SetAsync(
        bool on,
        IReadOnlyList<int> channels,
        TimeSpan failsafe,
        string summary,
        bool ordered,
        CancellationToken ct)
    {
        lock (_gate)
        {
            if (_latch.Phase != LatchPhase.Idle || _pendingRestore is not null)
            {
                activity.Info("Lights switched explicitly; no longer waiting for a video to end");
            }

            _latch.Disarm();
            CancelPendingRestore();
        }

        return await SwitchAsync(on, channels, failsafe > TimeSpan.Zero ? failsafe : null, summary, ordered, ct);
    }

    private async Task<ActionOutcome> OffForVideoAsync(
        IReadOnlyList<int> channels,
        TimeSpan failsafeMargin,
        TimeSpan restoreDelay,
        string summary,
        bool ordered,
        CancellationToken ct)
    {
        // What the layer shows right now, so the latch can tell the previous video from the new one.
        var reading = await propresenter.GetTransportCurrentAsync(VideoLayer, ct);
        var baseline = reading.Media is { } media ? ToSample(media, time: 0) : null;

        lock (_gate)
        {
            CancelPendingRestore();
            _latch.Arm(DateTimeOffset.Now, baseline);
            _latchChannels = channels;
            _latchOrdered = ordered;
            _latchRestoreDelay = restoreDelay;
            _failsafeMargin = failsafeMargin;
        }

        // Until the video's length is known, the relay's own timer is set to cover the search window,
        // so the lights still return if this app dies before the video is found.
        TimeSpan? toggleAfter = failsafeMargin > TimeSpan.Zero ? VideoLatch.GiveUpAfter + failsafeMargin : null;
        var outcome = await SwitchAsync(false, channels, toggleAfter, summary, ordered, ct);

        Updated?.Invoke();
        return outcome;
    }

    /// <summary>Called by the watcher with each reading of the video layer while <see cref="IsWatching"/>.</summary>
    internal void Observe(MediaSample? sample, DateTimeOffset now)
    {
        LatchEvent latchEvent;
        IReadOnlyList<int> channels;
        bool ordered;
        TimeSpan margin;
        TimeSpan restoreDelay;

        lock (_gate)
        {
            latchEvent = _latch.Observe(sample, now);
            channels = _latchChannels;
            ordered = _latchOrdered;
            margin = _failsafeMargin;
            restoreDelay = _latchRestoreDelay;
        }

        switch (latchEvent.Kind)
        {
            case LatchEventKind.Started:
                var video = latchEvent.Video!;
                activity.Info($"Lights stay off until '{video.Name}' ends",
                    video.Remaining is { } left ? $"{left:0}s remaining" : "length unknown");

                if (margin > TimeSpan.Zero && video.Remaining is { } remaining)
                {
                    // Re-arm the relay's own timer now the real length is known. Switching an output
                    // that is already off is harmless.
                    // The lights are already off, so there is no order to show.
                    _ = SwitchAsync(false, channels, TimeSpan.FromSeconds(remaining) + margin,
                        $"Failsafe for '{video.Name}'", ordered: false, CancellationToken.None);
                }

                Updated?.Invoke();
                break;

            case LatchEventKind.Ended:
                activity.Info($"'{latchEvent.Video!.Name}' ended — {latchEvent.Reason}");
                ScheduleRestore(channels, ordered, restoreDelay, $"'{latchEvent.Video.Name}' ended");
                break;

            case LatchEventKind.GaveUp:
                activity.Warning("Lights back on: " + latchEvent.Reason,
                    $"Check that this video plays on the {VideoLayer} layer, and look at the Activity page.");
                ScheduleRestore(channels, ordered, restoreDelay, "no video found");
                break;
        }
    }

    private void ScheduleRestore(IReadOnlyList<int> channels, bool ordered, TimeSpan delay, string reason)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            CancelPendingRestore();
            cts = _pendingRestore = new CancellationTokenSource();
        }

        Updated?.Invoke();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                if (_pendingRestore != cts) return;
                _pendingRestore = null;
            }

            var outcome = await SwitchAsync(true, channels, null, $"Lights on ({reason})", ordered, CancellationToken.None);
            if (outcome.Success) activity.Success(outcome.Summary);
            else activity.Error($"{outcome.Summary} failed", outcome.Error);
        });
    }

    /// <summary>Caller holds <see cref="_gate"/>.</summary>
    private void CancelPendingRestore()
    {
        _pendingRestore?.Cancel();
        _pendingRestore = null;
    }

    /// <param name="ordered">Follow the configured sequence; false switches every output together.</param>
    private async Task<ActionOutcome> SwitchAsync(
        bool on,
        IReadOnlyList<int> channels,
        TimeSpan? toggleAfter,
        string summary,
        bool ordered,
        CancellationToken ct)
    {
        var settings = config.Current.Lights;

        IReadOnlyList<LightStep> steps;
        try
        {
            steps = ordered
                ? LightSequence.Plan(settings.Sequence, channels, on)
                : [new LightStep(TimeSpan.Zero, null, channels)];
        }
        catch (FormatException ex)
        {
            return new ActionOutcome(false, summary, "Settings → FOH lights → Order: " + ex.Message);
        }

        // Each step starts on schedule rather than after the previous one answers, so the gaps are
        // the configured ones and not those plus the device's response time.
        var clock = Stopwatch.StartNew();
        var sent = new List<Task<IReadOnlyList<ShellySwitchResult>>>(steps.Count);
        foreach (var step in steps)
        {
            var wait = step.At - clock.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            sent.Add(shelly.SetAsync(settings.Host, settings.Password, step.Channels, on, toggleAfter, ct));
        }

        var results = (await Task.WhenAll(sent)).SelectMany(r => r).ToList();

        lock (_gate)
        {
            _lastCommandedOn = on;
            _lastChange = DateTimeOffset.Now;
        }

        Updated?.Invoke();

        var failed = results.Where(r => !r.Success).ToList();
        if (failed.Count == 0) return new ActionOutcome(true, summary);

        logger.LogWarning("Lights {State}: {Count} channel(s) failed", on ? "on" : "off", failed.Count);
        return new ActionOutcome(false, summary,
            string.Join("; ", failed.Select(f => $"channel {f.Channel}: {f.Error}")));
    }

    internal static MediaSample ToSample(TransportMedia media, double time) =>
        new(media.Uuid!, media.Name ?? "(unnamed)", media.IsPlaying, time, media.Duration);

    /// <summary>Parses <c>all</c> (meaning <paramref name="configured"/>) or a comma-separated list of outputs.</summary>
    internal static IReadOnlyList<int> ParseChannels(string? spec, string configured)
    {
        var source = string.IsNullOrWhiteSpace(spec) || spec.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            ? configured
            : spec;

        var channels = new List<int>();
        foreach (var part in source.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel) || channel < 0)
            {
                throw new FormatException($"'{part}' is not a relay output number.");
            }

            if (!channels.Contains(channel)) channels.Add(channel);
        }

        return channels.Count > 0 ? channels : throw new FormatException("No relay outputs to switch.");
    }

    /// <summary>Fractional seconds, clamped to 0–<paramref name="max"/>; anything unreadable is <paramref name="fallback"/>.</summary>
    internal static TimeSpan ParseDuration(string? value, TimeSpan fallback, TimeSpan max)
    {
        if (!double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return fallback;
        }

        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration > max ? max : duration;
    }

    private static TimeSpan ParseSeconds(string? value) =>
        ActionExecutor.TryParseInt(value, out var seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
}

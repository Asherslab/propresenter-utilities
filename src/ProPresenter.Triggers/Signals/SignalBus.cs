using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ProPresenter.Triggers.Signals;

/// <summary>One signal observed by the app, whatever delivered it.</summary>
/// <param name="Key">Normalised signal key, e.g. <c>GPI 1</c> or <c>webhook:start-service</c>.</param>
/// <param name="Origin">Where it came from, for display — a listener name and remote address.</param>
/// <param name="MatchedRules">How many armed rules fired. Zero means nothing is listening for it yet.</param>
public sealed record SignalEvent(DateTimeOffset At, string Key, string Origin, int MatchedRules);

/// <summary>
/// Single entry point for every external signal, and a short history of what has arrived.
/// </summary>
/// <remarks>
/// The history is what makes setup tractable: rather than asking an operator to know that their macro
/// emits <c>GPI 4</c>, the UI shows signals as they arrive so a rule can be attached to one by
/// clicking it. Signals are recorded whether or not any rule matched.
/// </remarks>
public sealed class SignalBus(TriggerEngine engine, ActivityLog activity, ILogger<SignalBus> logger)
{
    private const int HistoryLimit = 100;

    private readonly ConcurrentQueue<SignalEvent> _history = new();

    /// <summary>Raised when a signal arrives, so the Signals page can update live.</summary>
    public event Action? Changed;

    /// <summary>Most recent signals first.</summary>
    public IReadOnlyList<SignalEvent> Recent => _history.Reverse().ToList();

    /// <summary>Distinct signal keys seen since start-up, most recent first.</summary>
    public IReadOnlyList<string> KnownKeys =>
        _history.Reverse()
            .Select(e => e.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Records a signal and fires any rules attached to it.</summary>
    public void Publish(string key, string origin)
    {
        key = key.Trim();
        if (key.Length == 0) return;

        int matched;
        try
        {
            matched = engine.HandleSignal(key, origin);
        }
        catch (Exception ex)
        {
            // A failure dispatching one signal must not stop the listener handling the next.
            logger.LogError(ex, "Failed to dispatch signal {Key}", key);
            activity.Error($"Signal {key} could not be dispatched", ex.Message);
            matched = 0;
        }

        _history.Enqueue(new SignalEvent(DateTimeOffset.Now, key, origin, matched));
        while (_history.Count > HistoryLimit && _history.TryDequeue(out _)) { }

        if (matched == 0)
        {
            // Worth surfacing: during setup this is the difference between "ProPresenter never sent
            // it" and "it arrived but no rule is listening".
            activity.Info($"Signal {key} received — no rule is listening for it", origin);
        }

        logger.LogInformation("Signal {Key} from {Origin} matched {Count} rule(s)", key, origin, matched);
        Changed?.Invoke();
    }
}

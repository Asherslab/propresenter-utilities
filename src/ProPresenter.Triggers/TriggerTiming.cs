using ProPresenter.Api;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers;

/// <summary>
/// Works out how far a running timer is from a rule's firing point, for the dashboard's
/// "what happens next" list. Presentation only — the engine never consults this.
/// </summary>
public static class TriggerTiming
{
    /// <summary>
    /// Time until the rule fires. Negative once the point has passed, and null for rules that are not
    /// time-based or whose position cannot be determined from the current tick.
    /// </summary>
    public static TimeSpan? TimeUntil(TriggerRule rule, TimerObservation observation, TimerKind kind) =>
        rule.Kind switch
        {
            TriggerKind.AtRemaining when observation.Remaining is { } remaining => remaining - rule.Offset,
            TriggerKind.AtElapsed when observation.Elapsed is { } elapsed => rule.Offset - elapsed,

            // The clock runs down for countdowns and up for elapsed timers, so the sign flips.
            TriggerKind.AtTimerValue => kind == TimerKind.Elapsed
                ? rule.Offset - observation.Value
                : observation.Value - rule.Offset,

            // "When it ends" is due at zero remaining, where that is known.
            TriggerKind.OnEnd when observation.Remaining is { } remaining => remaining,

            _ => null,
        };

    /// <summary>Orders a timer's rules the way they will fire, with untimed rules last.</summary>
    public static IEnumerable<(TriggerRule Rule, TimeSpan? Until)> Schedule(
        IEnumerable<TriggerRule> rules,
        TimerObservation observation,
        TimerKind kind) =>
        rules
            .Select(rule => (Rule: rule, Until: TimeUntil(rule, observation, kind)))
            .OrderBy(entry => entry.Until is null)
            .ThenBy(entry => entry.Until ?? TimeSpan.MaxValue);
}

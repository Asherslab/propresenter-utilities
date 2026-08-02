using ProPresenter.Api;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers;

/// <summary>Why the evaluator did or did not fire anything for a tick.</summary>
public enum TickDisposition
{
    /// <summary>Rules were evaluated normally.</summary>
    Evaluated,

    /// <summary>First sample for this timer — recorded as a baseline, nothing fired.</summary>
    Baseline,

    /// <summary>The timer jumped backwards (reset, or time added). Re-baselined, nothing fired.</summary>
    ReBaselined,
}

public sealed record EvaluationResult(TickDisposition Disposition, IReadOnlyList<TriggerRule> Fired)
{
    public static EvaluationResult Baseline { get; } = new(TickDisposition.Baseline, []);
    public static EvaluationResult ReBaselined { get; } = new(TickDisposition.ReBaselined, []);
}

/// <summary>
/// Decides which rules fire for a given timer tick. Pure: no I/O, no clock, no state of its own.
/// </summary>
/// <remarks>
/// <para>
/// Time-based rules are <em>edge triggered on crossing</em>, never on equality. ProPresenter ticks at
/// whole-second resolution about once a second, but a tick can be late or dropped under load; an
/// equality test would silently skip the cue when that happens, whereas a crossing test still fires on
/// the following tick.
/// </para>
/// <para>
/// Because a crossing can only occur on the single tick that straddles the threshold, rules need no
/// "already fired" bookkeeping. This also gives the required reconnect behaviour for free: the first
/// tick after connecting is recorded as a baseline, so points already passed are never fired late.
/// </para>
/// </remarks>
public static class TriggerEvaluator
{
    /// <summary>
    /// A backwards jump larger than this is treated as a reset rather than normal counting. One second
    /// of slack absorbs rounding in the whole-second clock; real resets move much further.
    /// </summary>
    public static readonly TimeSpan ResetTolerance = TimeSpan.FromSeconds(1.5);

    public static EvaluationResult Evaluate(
        TimerObservation? previous,
        TimerObservation current,
        IReadOnlyList<TriggerRule> rules)
    {
        // No prior sample: record where the timer is and arm only what lies ahead.
        if (previous is not { } prior) return EvaluationResult.Baseline;

        // The timer went backwards: a reset, or seconds added via the increment endpoint. Re-arm
        // everything ahead of the new position without firing anything on the way back.
        //
        // Only the value-based kinds are suppressed. A jump moves the clock, not the state, so a
        // state transition on the same tick is real and must still fire. The two coincide whenever
        // ProPresenter applies a configuration and starts a timer in one action — a playlist header
        // with a timer attached and "Set Configuration" enabled does exactly that, landing the new
        // duration and the transition into running on the same tick.
        var reset = current.Progress < prior.Progress - ResetTolerance.TotalSeconds;

        var fired = new List<TriggerRule>();

        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;
            if (reset && !IsStateTransition(rule.Kind)) continue;
            if (Matches(rule, prior, current)) fired.Add(rule);
        }

        return new EvaluationResult(reset ? TickDisposition.ReBaselined : TickDisposition.Evaluated, fired);
    }

    /// <summary>
    /// Whether a kind is decided purely by the reported state, and so is unaffected by the clock
    /// jumping.
    /// </summary>
    private static bool IsStateTransition(TriggerKind kind) => kind is
        TriggerKind.OnStart or
        TriggerKind.OnStop or
        TriggerKind.OnEnd or
        TriggerKind.OnOverran or
        TriggerKind.OnStateChange;

    private static bool Matches(TriggerRule rule, TimerObservation previous, TimerObservation current) =>
        rule.Kind switch
        {
            TriggerKind.OnStart =>
                previous.State != TimerState.Running && current.State == TimerState.Running,

            TriggerKind.OnStop =>
                previous.State is TimerState.Running or TimerState.Overrunning
                && current.State == TimerState.Stopped,

            // "Ended" is whichever terminal transition the timer actually makes: a timer without
            // overrun goes straight to complete, one with overrun goes to overrunning first.
            TriggerKind.OnEnd =>
                previous.State is not (TimerState.Complete or TimerState.Overrunning or TimerState.Overran)
                && current.State is TimerState.Complete or TimerState.Overrunning,

            TriggerKind.OnOverran =>
                previous.State != TimerState.Overran && current.State == TimerState.Overran,

            TriggerKind.OnStateChange =>
                previous.State != rule.TargetState && current.State == rule.TargetState,

            TriggerKind.AtTimerValue =>
                Crossed(previous.Value, current.Value, rule.Offset),

            TriggerKind.AtElapsed =>
                Crossed(previous.Elapsed, current.Elapsed, rule.Offset),

            TriggerKind.AtRemaining =>
                Crossed(previous.Remaining, current.Remaining, rule.Offset),

            _ => false,
        };

    private static bool Crossed(TimeSpan? previous, TimeSpan? current, TimeSpan threshold) =>
        previous.HasValue && current.HasValue && Crossed(previous.Value, current.Value, threshold);

    /// <summary>
    /// True when the value moved onto or past <paramref name="threshold"/> since the previous sample,
    /// in either direction. Sitting exactly on the threshold across both samples is not a crossing.
    /// </summary>
    internal static bool Crossed(TimeSpan previous, TimeSpan current, TimeSpan threshold) =>
        (previous > threshold && current <= threshold) || (previous < threshold && current >= threshold);
}

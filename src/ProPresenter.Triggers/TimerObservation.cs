using ProPresenter.Api;

namespace ProPresenter.Triggers;

/// <summary>
/// A single tick for one timer, normalised so rules can be evaluated without knowing the timer's kind.
/// </summary>
/// <param name="State">Reported state.</param>
/// <param name="Value">The displayed clock, exactly as ProPresenter reports it.</param>
/// <param name="Elapsed">Time since the timer started, where it can be determined.</param>
/// <param name="Remaining">Time until the timer's end, where it can be determined.</param>
/// <param name="Progress">
/// A monotonically increasing measure of how far the timer has advanced, in seconds. Countdown timers
/// count down and elapsed timers count up, so the raw clock is not comparable across kinds; this is.
/// A decrease means the timer was reset or had time added to it.
/// </param>
public readonly record struct TimerObservation(
    TimerState State,
    TimeSpan Value,
    TimeSpan? Elapsed,
    TimeSpan? Remaining,
    double Progress)
{
    /// <summary>
    /// Normalises a raw stream sample against the timer's configuration.
    /// </summary>
    /// <param name="trackedElapsed">
    /// Elapsed time measured by this application from when it first saw the timer running. Used for
    /// count-down-to-time timers, whose total length depends on the wall clock when they start and so
    /// cannot be derived from configuration alone.
    /// </param>
    public static TimerObservation Create(
        TimerConfiguration? configuration,
        TimerCurrent current,
        TimeSpan? trackedElapsed = null)
    {
        var value = current.Value;
        var kind = configuration?.Kind ?? TimerKind.Unknown;

        TimeSpan? elapsed;
        TimeSpan? remaining;
        double progress;

        switch (kind)
        {
            case TimerKind.Countdown:
                // The clock is the remaining time and runs down to zero (negative during overrun).
                remaining = value;
                elapsed = configuration!.TotalDuration - value;
                progress = -value.TotalSeconds;
                break;

            case TimerKind.CountDownToTime:
                // Also counts down, but the total length is only known once it starts.
                remaining = value;
                elapsed = trackedElapsed;
                progress = -value.TotalSeconds;
                break;

            case TimerKind.Elapsed:
                // Counts up from start_time towards the optional end_time.
                elapsed = value - TimeSpan.FromSeconds(configuration!.Elapsed!.StartTime);
                remaining = configuration.Elapsed.EndTime is > 0
                    ? TimeSpan.FromSeconds(configuration.Elapsed.EndTime!.Value) - value
                    : null;
                progress = value.TotalSeconds;
                break;

            default:
                // Unknown configuration: fall back to the raw clock and offer no derived values.
                elapsed = trackedElapsed;
                remaining = null;
                progress = value.TotalSeconds;
                break;
        }

        return new TimerObservation(current.State, value, elapsed, remaining, progress);
    }

    /// <summary>True for the states in which a timer is actively counting.</summary>
    public bool IsCounting => State is TimerState.Running or TimerState.Overrunning;
}

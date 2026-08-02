using ProPresenter.Api;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers.Tests;

/// <summary>
/// Replays a sequence of ticks through the evaluator exactly as <see cref="TriggerEngine"/> would,
/// so tests exercise the same baseline and re-baseline handling the live engine uses.
/// </summary>
internal sealed class TimerScenario(TimerConfiguration configuration)
{
    private static readonly ProId TimerId = new()
    {
        Uuid = "11111111-2222-3333-4444-555555555555",
        Name = "Test Timer",
        Index = 0,
    };

    private readonly List<TriggerRule> _rules = [];
    private TimerObservation? _previous;

    public List<string> Fired { get; } = [];
    public List<TickDisposition> Dispositions { get; } = [];

    public static TimerScenario Countdown(int durationSeconds, bool allowsOverrun = false) =>
        new(new TimerConfiguration
        {
            Id = TimerId,
            AllowsOverrun = allowsOverrun,
            Countdown = new CountdownConfig { Duration = durationSeconds },
        });

    public static TimerScenario Elapsed(int startSeconds, int? endSeconds) =>
        new(new TimerConfiguration
        {
            Id = TimerId,
            Elapsed = new ElapsedConfig { StartTime = startSeconds, EndTime = endSeconds },
        });

    public static TimerScenario CountDownToTime() =>
        new(new TimerConfiguration
        {
            Id = TimerId,
            CountDownToTime = new CountdownToTimeConfig { TimeOfDay = 18000, Period = "pm" },
        });

    public TimerScenario WithRule(TriggerKind kind, TimeSpan offset = default, string? name = null,
        bool enabled = true, TimerState targetState = TimerState.Complete)
    {
        _rules.Add(new TriggerRule
        {
            Name = name ?? $"{kind}@{offset}",
            Kind = kind,
            Offset = offset,
            Enabled = enabled,
            TargetState = targetState,
            Timer = TimerRef.From(TimerId),
        });
        return this;
    }

    /// <summary>Feeds one tick. <paramref name="clock"/> is the value ProPresenter would display.</summary>
    public TimerScenario Tick(TimeSpan clock, TimerState state, TimeSpan? trackedElapsed = null)
    {
        var current = new TimerCurrent
        {
            Id = TimerId,
            Time = TimeCode.Format(clock),
            StateRaw = state.ToString().ToLowerInvariant(),
        };

        var observation = TimerObservation.Create(configuration, current, trackedElapsed);
        var result = TriggerEvaluator.Evaluate(_previous, observation, _rules);

        Dispositions.Add(result.Disposition);
        Fired.AddRange(result.Fired.Select(r => r.Name));
        _previous = observation;
        return this;
    }

    public TimerScenario Tick(int clockSeconds, TimerState state, TimeSpan? trackedElapsed = null) =>
        Tick(TimeSpan.FromSeconds(clockSeconds), state, trackedElapsed);

    /// <summary>Simulates a process restart or stream reconnect: the timer keeps running, we forget everything.</summary>
    public TimerScenario Reconnect()
    {
        _previous = null;
        return this;
    }
}

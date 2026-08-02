using ProPresenter.Api;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers.Tests;

public class TriggerEvaluatorTests
{
    // ------------------------------------------------------------- crossing

    [Theory]
    [InlineData(65, 55, 60, true)]   // descending straight through the threshold
    [InlineData(61, 60, 60, true)]   // descending onto it exactly
    [InlineData(60, 59, 60, false)]  // already there on the previous tick
    [InlineData(60, 60, 60, false)]  // parked on it
    [InlineData(59, 58, 60, false)]  // already past
    [InlineData(55, 65, 60, true)]   // ascending through it (elapsed timers count up)
    [InlineData(59, 60, 60, true)]   // ascending onto it exactly
    public void Crossing_is_edge_triggered_in_both_directions(int previous, int current, int threshold, bool expected)
    {
        var crossed = TriggerEvaluator.Crossed(
            TimeSpan.FromSeconds(previous),
            TimeSpan.FromSeconds(current),
            TimeSpan.FromSeconds(threshold));

        Assert.Equal(expected, crossed);
    }

    // -------------------------------------------------------- the example workflow

    [Fact]
    public void Countdown_fires_start_then_sixty_seconds_out_then_end()
    {
        // A 5-minute countdown: backing track at the start, countdown music at T-60, advance at zero.
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.OnStart, name: "backing-track")
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "countdown-music")
            .WithRule(TriggerKind.OnEnd, name: "next-presentation");

        scenario.Tick(300, TimerState.Stopped);
        scenario.Tick(300, TimerState.Running);
        for (var remaining = 299; remaining >= 0; remaining--)
        {
            scenario.Tick(remaining, remaining == 0 ? TimerState.Complete : TimerState.Running);
        }

        Assert.Equal(["backing-track", "countdown-music", "next-presentation"], scenario.Fired);
    }

    [Fact]
    public void Relative_to_end_fires_exactly_once_across_a_full_run()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "cue");

        scenario.Tick(300, TimerState.Running);
        for (var remaining = 299; remaining >= 0; remaining--)
        {
            scenario.Tick(remaining, remaining == 0 ? TimerState.Complete : TimerState.Running);
        }

        Assert.Equal(["cue"], scenario.Fired);
    }

    [Fact]
    public void A_dropped_tick_still_fires_the_cue()
    {
        // ProPresenter ticks about once a second, but a late or dropped tick must not skip a cue.
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "cue");

        scenario.Tick(70, TimerState.Running);
        scenario.Tick(52, TimerState.Running); // jumped straight over 60

        Assert.Equal(["cue"], scenario.Fired);
    }

    // ------------------------------------------------------------- baseline

    [Fact]
    public void The_first_tick_never_fires()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "cue")
            .WithRule(TriggerKind.OnStart, name: "start");

        scenario.Tick(60, TimerState.Running);

        Assert.Empty(scenario.Fired);
        Assert.Equal(TickDisposition.Baseline, scenario.Dispositions[0]);
    }

    [Fact]
    public void Reconnecting_mid_run_skips_points_already_passed_and_arms_the_rest()
    {
        var scenario = TimerScenario.Countdown(600)
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromMinutes(5), "five-minute-warning")
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "countdown-music")
            .WithRule(TriggerKind.OnEnd, name: "advance");

        scenario.Tick(600, TimerState.Running);
        scenario.Tick(200, TimerState.Running); // the five-minute warning fires here
        Assert.Equal(["five-minute-warning"], scenario.Fired);

        // Service restarts with 100 seconds left.
        scenario.Reconnect();
        scenario.Tick(100, TimerState.Running);
        scenario.Tick(61, TimerState.Running);
        scenario.Tick(59, TimerState.Running);
        scenario.Tick(0, TimerState.Complete);

        // The already-passed warning is not replayed; everything still ahead fires normally.
        Assert.Equal(["five-minute-warning", "countdown-music", "advance"], scenario.Fired);
    }

    // ------------------------------------------------------------- resetting

    [Fact]
    public void Resetting_a_timer_does_not_fire_on_the_way_back_and_re_arms()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "cue");

        scenario.Tick(300, TimerState.Running);
        scenario.Tick(61, TimerState.Running);
        scenario.Tick(59, TimerState.Running);
        Assert.Equal(["cue"], scenario.Fired);

        // Reset back to the top: the clock jumps from 59 to 300, crossing 60 upwards.
        scenario.Tick(300, TimerState.Stopped);
        Assert.Contains(TickDisposition.ReBaselined, scenario.Dispositions);
        Assert.Single(scenario.Fired);

        // Running it again fires the cue a second time, as it should.
        scenario.Tick(299, TimerState.Running);
        scenario.Tick(61, TimerState.Running);
        scenario.Tick(59, TimerState.Running);
        Assert.Equal(["cue", "cue"], scenario.Fired);
    }

    [Fact]
    public void Adding_time_to_a_running_timer_re_arms_cues_that_are_now_ahead_again()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "countdown-music");

        scenario.Tick(70, TimerState.Running);
        scenario.Tick(59, TimerState.Running);
        Assert.Equal(["countdown-music"], scenario.Fired);

        // The operator adds two minutes; the cue point is ahead again.
        scenario.Tick(179, TimerState.Running);
        Assert.Single(scenario.Fired);

        scenario.Tick(61, TimerState.Running);
        scenario.Tick(59, TimerState.Running);
        Assert.Equal(["countdown-music", "countdown-music"], scenario.Fired);
    }

    [Fact]
    public void Starting_a_timer_fires_even_when_the_clock_jumps_on_the_same_tick()
    {
        // A playlist header with a timer attached and "Set Configuration" enabled applies the new
        // duration and starts the timer in one action, so the tick that turns the timer running also
        // carries a large backwards jump. The start still happened.
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.OnStart, name: "backing-track")
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "countdown-music");

        // Left over from the last time the timer ran, under its previous configuration.
        scenario.Tick(60, TimerState.Stopped);

        // The header sets it to five minutes and starts it.
        scenario.Tick(300, TimerState.Running);

        Assert.Contains(TickDisposition.ReBaselined, scenario.Dispositions);
        Assert.Equal(["backing-track"], scenario.Fired);

        // The value rule was re-armed rather than fired on the jump, and still fires on the way down.
        scenario.Tick(61, TimerState.Running);
        scenario.Tick(59, TimerState.Running);
        Assert.Equal(["backing-track", "countdown-music"], scenario.Fired);
    }

    [Fact]
    public void Resetting_a_running_timer_to_stopped_still_fires_on_stop()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.OnStop, name: "stopped");

        scenario.Tick(59, TimerState.Running);
        scenario.Tick(300, TimerState.Stopped); // stop resets the clock to the top

        Assert.Equal(["stopped"], scenario.Fired);
    }

    [Fact]
    public void Sub_second_jitter_is_not_mistaken_for_a_reset()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "cue");

        scenario.Tick(TimeSpan.FromSeconds(61), TimerState.Running);
        scenario.Tick(TimeSpan.FromSeconds(61.4), TimerState.Running); // clock nudged backwards slightly
        scenario.Tick(TimeSpan.FromSeconds(59), TimerState.Running);

        Assert.DoesNotContain(TickDisposition.ReBaselined, scenario.Dispositions);
        Assert.Equal(["cue"], scenario.Fired);
    }

    // ---------------------------------------------------------------- states

    [Fact]
    public void OnEnd_fires_on_complete_when_overrun_is_disabled()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.OnEnd, name: "end");

        scenario.Tick(1, TimerState.Running);
        scenario.Tick(0, TimerState.Complete);

        Assert.Equal(["end"], scenario.Fired);
    }

    [Fact]
    public void OnEnd_fires_once_on_entering_overrun_when_overrun_is_enabled()
    {
        var scenario = TimerScenario.Countdown(300, allowsOverrun: true)
            .WithRule(TriggerKind.OnEnd, name: "end")
            .WithRule(TriggerKind.OnOverran, name: "overran");

        scenario.Tick(1, TimerState.Running);
        scenario.Tick(0, TimerState.Overrunning);
        scenario.Tick(-1, TimerState.Overrunning);
        scenario.Tick(-2, TimerState.Overrunning);
        scenario.Tick(-3, TimerState.Overran);

        // "End" means the timer reached zero, whichever terminal state it moved into.
        Assert.Equal(["end", "overran"], scenario.Fired);
    }

    [Fact]
    public void OnStart_and_OnStop_track_the_running_state()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.OnStart, name: "start")
            .WithRule(TriggerKind.OnStop, name: "stop");

        scenario.Tick(300, TimerState.Stopped);
        scenario.Tick(300, TimerState.Running);
        scenario.Tick(299, TimerState.Running);
        scenario.Tick(298, TimerState.Stopped);

        Assert.Equal(["start", "stop"], scenario.Fired);
    }

    [Fact]
    public void OnStop_does_not_fire_when_a_timer_simply_completes()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.OnStop, name: "stop");

        scenario.Tick(1, TimerState.Running);
        scenario.Tick(0, TimerState.Complete);

        Assert.Empty(scenario.Fired);
    }

    [Fact]
    public void OnStateChange_watches_an_arbitrary_state()
    {
        var scenario = TimerScenario.Countdown(300, allowsOverrun: true)
            .WithRule(TriggerKind.OnStateChange, targetState: TimerState.Overrunning, name: "overrunning");

        scenario.Tick(1, TimerState.Running);
        scenario.Tick(0, TimerState.Overrunning);
        scenario.Tick(-1, TimerState.Overrunning);

        Assert.Equal(["overrunning"], scenario.Fired);
    }

    // ----------------------------------------------------------- timer kinds

    [Fact]
    public void Elapsed_timers_support_relative_to_start_and_relative_to_end()
    {
        // Counts up from 0 towards 120.
        var scenario = TimerScenario.Elapsed(0, 120)
            .WithRule(TriggerKind.AtElapsed, TimeSpan.FromSeconds(30), "thirty-in")
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(15), "fifteen-left");

        scenario.Tick(0, TimerState.Running);
        for (var value = 1; value <= 120; value++)
        {
            scenario.Tick(value, value == 120 ? TimerState.Complete : TimerState.Running);
        }

        Assert.Equal(["thirty-in", "fifteen-left"], scenario.Fired);
    }

    [Fact]
    public void Elapsed_timers_without_an_end_ignore_relative_to_end_rules()
    {
        var scenario = TimerScenario.Elapsed(0, endSeconds: null)
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(15), "fifteen-left")
            .WithRule(TriggerKind.AtElapsed, TimeSpan.FromSeconds(10), "ten-in");

        scenario.Tick(0, TimerState.Running);
        for (var value = 1; value <= 60; value++) scenario.Tick(value, TimerState.Running);

        // No end time means "remaining" is undefined; the rule stays silent rather than guessing.
        Assert.Equal(["ten-in"], scenario.Fired);
    }

    [Fact]
    public void Countdown_to_time_supports_relative_to_end_and_tracked_elapsed()
    {
        var scenario = TimerScenario.CountDownToTime()
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "one-minute-left")
            .WithRule(TriggerKind.AtElapsed, TimeSpan.FromSeconds(30), "thirty-in");

        scenario.Tick(120, TimerState.Running, TimeSpan.Zero);
        scenario.Tick(100, TimerState.Running, TimeSpan.FromSeconds(20));
        scenario.Tick(80, TimerState.Running, TimeSpan.FromSeconds(40));  // crosses 30s elapsed
        scenario.Tick(50, TimerState.Running, TimeSpan.FromSeconds(70));  // crosses 60s remaining

        Assert.Equal(["thirty-in", "one-minute-left"], scenario.Fired);
    }

    [Fact]
    public void Absolute_clock_rules_work_regardless_of_timer_kind()
    {
        var scenario = TimerScenario.Countdown(600)
            .WithRule(TriggerKind.AtTimerValue, TimeSpan.FromMinutes(5), "at-five-minutes");

        scenario.Tick(600, TimerState.Running);
        scenario.Tick(301, TimerState.Running);
        scenario.Tick(299, TimerState.Running);

        Assert.Equal(["at-five-minutes"], scenario.Fired);
    }

    // --------------------------------------------------------------- gating

    [Fact]
    public void Disabled_rules_never_fire()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "cue", enabled: false)
            .WithRule(TriggerKind.OnEnd, name: "end", enabled: false);

        scenario.Tick(300, TimerState.Running);
        scenario.Tick(59, TimerState.Running);
        scenario.Tick(0, TimerState.Complete);

        Assert.Empty(scenario.Fired);
    }

    [Fact]
    public void Multiple_rules_on_one_timer_all_fire_at_their_own_points()
    {
        var scenario = TimerScenario.Countdown(300)
            .WithRule(TriggerKind.OnStart, name: "a")
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(120), "b")
            .WithRule(TriggerKind.AtRemaining, TimeSpan.FromSeconds(60), "c")
            .WithRule(TriggerKind.AtTimerValue, TimeSpan.FromSeconds(10), "d")
            .WithRule(TriggerKind.OnEnd, name: "e");

        scenario.Tick(300, TimerState.Stopped);
        scenario.Tick(300, TimerState.Running);
        for (var remaining = 299; remaining >= 0; remaining--)
        {
            scenario.Tick(remaining, remaining == 0 ? TimerState.Complete : TimerState.Running);
        }

        Assert.Equal(["a", "b", "c", "d", "e"], scenario.Fired);
    }
}

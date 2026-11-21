using ProPresenter.Triggers.Lights;

namespace ProPresenter.Triggers.Tests;

public class VideoLatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private static MediaSample Video(string uuid, double time, bool playing = true, double duration = 120) =>
        new(uuid, uuid, playing, time, duration);

    private static VideoLatch ArmedAt(MediaSample? current = null)
    {
        var latch = new VideoLatch();
        latch.Arm(T0, current);
        return latch;
    }

    [Fact]
    public void Adopts_a_video_that_has_just_started()
    {
        var latch = ArmedAt();

        var result = latch.Observe(Video("A", 0.4), T0.AddMilliseconds(500));

        Assert.Equal(LatchEventKind.Started, result.Kind);
        Assert.Equal(LatchPhase.Tracking, latch.Phase);
        Assert.Equal("A", latch.Video!.Uuid);
    }

    [Fact]
    public void Does_not_mistake_the_previous_video_for_the_new_one()
    {
        // The macro ran on the new cue while the layer still showed the old video, well into it.
        var latch = ArmedAt(Video("OLD", 95));

        Assert.Equal(LatchEventKind.None, latch.Observe(Video("OLD", 95.5), T0.AddMilliseconds(500)).Kind);
        Assert.Equal(LatchEventKind.Started, latch.Observe(Video("NEW", 0.5), T0.AddSeconds(1)).Kind);
        Assert.Equal("NEW", latch.Video!.Uuid);
    }

    [Fact]
    public void Adopts_whatever_is_playing_after_a_short_wait()
    {
        // Rule fired by hand halfway through a video.
        var latch = ArmedAt(Video("A", 40));

        Assert.Equal(LatchEventKind.None, latch.Observe(Video("A", 41), T0.AddSeconds(1)).Kind);
        Assert.Equal(LatchEventKind.Started, latch.Observe(Video("A", 43), T0 + VideoLatch.AdoptAnyAfter).Kind);
    }

    [Fact]
    public void Gives_up_when_no_video_appears()
    {
        var latch = ArmedAt();

        Assert.Equal(LatchEventKind.None, latch.Observe(null, T0.AddSeconds(5)).Kind);
        var result = latch.Observe(null, T0 + VideoLatch.GiveUpAfter);

        Assert.Equal(LatchEventKind.GaveUp, result.Kind);
        Assert.Equal(LatchPhase.Idle, latch.Phase);
    }

    [Fact]
    public void A_paused_video_is_not_waiting_for_anything()
    {
        var latch = ArmedAt();

        Assert.Equal(LatchEventKind.None, latch.Observe(Video("A", 2, playing: false), T0.AddSeconds(1)).Kind);
        Assert.Equal(LatchPhase.WaitingForVideo, latch.Phase);
    }

    [Fact]
    public void Ends_when_the_layer_is_cleared()
    {
        var latch = Tracking("A");

        var result = latch.Observe(null, T0.AddSeconds(30));

        Assert.Equal(LatchEventKind.Ended, result.Kind);
        Assert.Equal("A", result.Video!.Uuid);
        Assert.Equal(LatchPhase.Idle, latch.Phase);
    }

    [Fact]
    public void Ends_when_another_item_replaces_it()
    {
        var latch = Tracking("A");

        Assert.Equal(LatchEventKind.Ended, latch.Observe(Video("B", 0.2), T0.AddSeconds(30)).Kind);
    }

    [Fact]
    public void Ends_when_it_stops_at_its_end()
    {
        var latch = Tracking("A");

        Assert.Equal(LatchEventKind.None, latch.Observe(Video("A", 119.2), T0.AddSeconds(119)).Kind);
        Assert.Equal(LatchEventKind.Ended,
            latch.Observe(Video("A", 119.6, playing: false), T0.AddSeconds(120)).Kind);
    }

    [Fact]
    public void Ends_when_it_rewinds_to_the_start_and_stops()
    {
        var latch = Tracking("A");
        latch.Observe(Video("A", 60), T0.AddSeconds(60));

        Assert.Equal(LatchEventKind.Ended, latch.Observe(Video("A", 0, playing: false), T0.AddSeconds(121)).Kind);
    }

    [Fact]
    public void Pausing_mid_video_does_not_end_it()
    {
        var latch = Tracking("A");

        Assert.Equal(LatchEventKind.None, latch.Observe(Video("A", 50, playing: false), T0.AddSeconds(50)).Kind);
        Assert.Equal(LatchPhase.Tracking, latch.Phase);
    }

    [Fact]
    public void A_looping_video_keeps_the_lights_off_until_cleared()
    {
        var latch = Tracking("A", duration: 10);

        Assert.Equal(LatchEventKind.None, latch.Observe(Video("A", 9.8, duration: 10), T0.AddSeconds(10)).Kind);
        Assert.Equal(LatchEventKind.None, latch.Observe(Video("A", 0.3, duration: 10), T0.AddSeconds(11)).Kind);
        Assert.Equal(LatchEventKind.Ended, latch.Observe(null, T0.AddSeconds(30)).Kind);
    }

    [Fact]
    public void Re_arming_starts_over_for_the_next_video()
    {
        var latch = Tracking("A");

        latch.Arm(T0.AddSeconds(30), Video("A", 30));

        Assert.Equal(LatchPhase.WaitingForVideo, latch.Phase);
        Assert.Equal(LatchEventKind.Started, latch.Observe(Video("B", 0.1), T0.AddSeconds(31)).Kind);
    }

    [Fact]
    public void Reports_remaining_time_once_the_length_is_known()
    {
        Assert.Equal(90, Video("A", 30).Remaining);
        Assert.Null(Video("A", 30, duration: 0).Remaining);
    }

    private static VideoLatch Tracking(string uuid, double duration = 120)
    {
        var latch = ArmedAt();
        Assert.Equal(LatchEventKind.Started, latch.Observe(Video(uuid, 0, duration: duration), T0).Kind);
        return latch;
    }
}

namespace ProPresenter.Triggers.Lights;

/// <summary>One reading of the presentation transport layer.</summary>
public sealed record MediaSample(string Uuid, string Name, bool IsPlaying, double Time, double Duration)
{
    /// <summary>Seconds left to play, where the length is known.</summary>
    public double? Remaining => Duration > 0 ? Math.Max(0, Duration - Time) : null;
}

public enum LatchPhase
{
    /// <summary>Nothing to restore.</summary>
    Idle,

    /// <summary>Lights went off for a video that has not been seen playing yet.</summary>
    WaitingForVideo,

    /// <summary>Lights are off until <see cref="VideoLatch.Video"/> ends.</summary>
    Tracking,
}

public enum LatchEventKind
{
    None,

    /// <summary>The video the lights went off for has been identified.</summary>
    Started,

    /// <summary>That video has ended; the lights should come back on.</summary>
    Ended,

    /// <summary>No video appeared in time; the lights should come back on.</summary>
    GaveUp,
}

public readonly record struct LatchEvent(LatchEventKind Kind, MediaSample? Video = null, string? Reason = null)
{
    public static readonly LatchEvent None = new(LatchEventKind.None);
}

/// <summary>
/// Decides when "lights off for this video" should give the lights back, from successive readings of
/// the presentation transport layer.
/// </summary>
/// <remarks>
/// <para>
/// The macro that arms the latch runs on the video's own cue, so the video may or may not already be
/// reported when the latch is armed — and the layer may still be showing the <em>previous</em> video
/// for a moment. A playing video is adopted straight away only if it differs from what was on the
/// layer when the latch was armed, or has only just started. After <see cref="AdoptAnyAfter"/>
/// whatever is playing is adopted, which covers firing the rule by hand mid-video.
/// </para>
/// <para>
/// The video counts as ended when the layer empties (cleared, or the video's end action is Clear),
/// when a different item replaces it, or when it stops at its end (end action Stop). Pausing
/// mid-video does not end it. Readings where ProPresenter did not answer are never passed in, so a
/// dropped request cannot bring the lights up mid-video.
/// </para>
/// Not thread-safe; the owner serialises access.
/// </remarks>
public sealed class VideoLatch
{
    public static readonly TimeSpan AdoptAnyAfter = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(15);

    /// <summary>A video at or below this position is treated as just started.</summary>
    private const double FreshStartSeconds = 3;

    /// <summary>A stopped video this close to its length has played out.</summary>
    private const double EndToleranceSeconds = 1;

    private DateTimeOffset _armedAt;
    private string? _baselineUuid;
    private double _furthest;

    public LatchPhase Phase { get; private set; } = LatchPhase.Idle;

    /// <summary>The video being waited on, once identified. Updated with each reading.</summary>
    public MediaSample? Video { get; private set; }

    /// <param name="current">What was on the layer as the lights went off, if known.</param>
    public void Arm(DateTimeOffset now, MediaSample? current)
    {
        Phase = LatchPhase.WaitingForVideo;
        Video = null;
        _armedAt = now;
        _baselineUuid = current?.Uuid;
        _furthest = 0;
    }

    public void Disarm()
    {
        Phase = LatchPhase.Idle;
        Video = null;
    }

    /// <param name="sample">The layer's item, or null when the layer is empty.</param>
    public LatchEvent Observe(MediaSample? sample, DateTimeOffset now)
    {
        switch (Phase)
        {
            case LatchPhase.WaitingForVideo:
                if (sample is { IsPlaying: true } candidate
                    && (!string.Equals(candidate.Uuid, _baselineUuid, StringComparison.OrdinalIgnoreCase)
                        || candidate.Time <= FreshStartSeconds
                        || now - _armedAt >= AdoptAnyAfter))
                {
                    Phase = LatchPhase.Tracking;
                    Video = candidate;
                    _furthest = candidate.Time;
                    return new LatchEvent(LatchEventKind.Started, candidate);
                }

                if (now - _armedAt >= GiveUpAfter)
                {
                    Disarm();
                    return new LatchEvent(LatchEventKind.GaveUp,
                        Reason: $"no video started on the presentation layer within {GiveUpAfter.TotalSeconds:0}s");
                }

                return LatchEvent.None;

            case LatchPhase.Tracking:
                var video = Video!;

                if (sample is null) return End(video, "the layer was cleared");

                if (!string.Equals(sample.Uuid, video.Uuid, StringComparison.OrdinalIgnoreCase))
                {
                    return End(video, $"'{sample.Name}' replaced it");
                }

                if (!sample.IsPlaying)
                {
                    if (sample.Duration > 0 && sample.Time >= sample.Duration - EndToleranceSeconds)
                    {
                        return End(sample, "it finished");
                    }

                    // Some end actions rewind to the start rather than holding the last frame.
                    if (sample.Time < 0.5 && _furthest > FreshStartSeconds)
                    {
                        return End(sample, "it finished");
                    }
                }

                _furthest = Math.Max(_furthest, sample.Time);
                Video = sample;
                return LatchEvent.None;

            default:
                return LatchEvent.None;
        }
    }

    private LatchEvent End(MediaSample video, string reason)
    {
        Disarm();
        return new LatchEvent(LatchEventKind.Ended, video, reason);
    }
}

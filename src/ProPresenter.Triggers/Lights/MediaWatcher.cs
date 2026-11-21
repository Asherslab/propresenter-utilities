using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProPresenter.Api;

namespace ProPresenter.Triggers.Lights;

/// <summary>
/// Polls the video layer while <see cref="LightsController"/> is waiting for a video to end, and does
/// nothing at all otherwise.
/// </summary>
/// <remarks>
/// Polling rather than a chunked stream: it only runs for the length of a video, a request every
/// half-second on the local network is negligible, and it needs no reconnection logic of its own.
/// </remarks>
public sealed class MediaWatcher(
    LightsController lights,
    ProPresenterClient propresenter,
    ILogger<MediaWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (lights.IsWatching)
                {
                    await PollOnceAsync(stoppingToken);
                    await Task.Delay(PollInterval, stoppingToken);
                }
                else
                {
                    await Task.Delay(IdleInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Video layer poll failed");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var reading = await propresenter.GetTransportCurrentAsync(LightsController.VideoLayer, ct);

        // No answer is not the same as an empty layer: skip the reading rather than end the video.
        // The latch's own give-up timer still runs off the next answered reading.
        if (!reading.Reachable) return;

        MediaSample? sample = null;
        if (reading.Media is { AudioOnly: false } media)
        {
            // A position of zero would read as "rewound to the start", so a failed read is skipped too.
            if (await propresenter.GetTransportTimeAsync(LightsController.VideoLayer, ct) is not { } time) return;
            sample = LightsController.ToSample(media, time);
        }

        lights.Observe(sample, now);
    }
}

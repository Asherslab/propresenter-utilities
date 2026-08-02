namespace ProPresenter.App.Services;

/// <summary>
/// Keeps the ProPresenter lists warm while somebody is using the UI.
/// </summary>
/// <remarks>
/// Without this, a macro created in ProPresenter only appears in the editor when a page happens to
/// ask for it. The loop ticks often but does almost nothing unless <see cref="UiActivity"/> says an
/// operator is present and a list has actually gone stale, so an idle service makes roughly one
/// request per list every ten minutes.
/// </remarks>
public sealed class LookupRefreshService(
    LookupCache lookups,
    UiActivity activity,
    ILogger<LookupRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = activity.IsActive ? ActivePollInterval : IdlePollInterval;

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Nothing has ever been looked up, or nobody is watching: skip the round trip entirely.
            if (lookups.TrackedKinds.Count == 0) continue;
            if (!activity.IsActive) continue;

            try
            {
                await lookups.RefreshStaleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Lookup refresh failed");
            }
        }
    }
}

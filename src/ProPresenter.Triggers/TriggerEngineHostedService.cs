using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProPresenter.Api;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers;

/// <summary>
/// Owns the lifetime of the timer stream and keeps timer definitions fresh.
/// </summary>
/// <remarks>
/// Definitions are re-read periodically as well as on connect, because a timer edited directly in
/// ProPresenter changes the meaning of every "relative to end" rule attached to it.
/// </remarks>
public sealed class TriggerEngineHostedService(
    TimerStream stream,
    TriggerEngine engine,
    ConfigurationStore config,
    ProPresenterEndpoint endpoint,
    ActivityLog activity,
    ILogger<TriggerEngineHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan DefinitionRefreshInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ApplyEndpoint(config.Current.Connection);
        config.Changed += OnConfigChanged;

        logger.LogInformation("Trigger engine starting against {Uri}", endpoint.BaseUri);
        activity.Info($"Watching timers at {endpoint.BaseUri.Authority}");

        var streamTask = stream.RunAsync(stoppingToken);
        var refreshTask = RefreshLoopAsync(stoppingToken);

        try
        {
            await Task.WhenAll(streamTask, refreshTask);
        }
        finally
        {
            config.Changed -= OnConfigChanged;
        }
    }

    private void OnConfigChanged(AppConfiguration configuration) => ApplyEndpoint(configuration.Connection);

    private void ApplyEndpoint(ConnectionSettings connection)
    {
        try
        {
            endpoint.Set(connection.Host, connection.Port);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Invalid connection settings {Host}:{Port}", connection.Host, connection.Port);
            activity.Error($"Invalid connection settings {connection.Host}:{connection.Port}", ex.Message);
        }
    }

    private async Task RefreshLoopAsync(CancellationToken stoppingToken)
    {
        using var ticker = new PeriodicTimer(DefinitionRefreshInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await engine.RefreshTimerConfigurationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Timer definition refresh failed");
            }

            try
            {
                if (!await ticker.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

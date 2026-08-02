using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers.Signals;

public enum ListenerState
{
    Disabled,
    Listening,
    Failed,
}

public sealed record ListenerStatus(
    ListenerState State,
    int Port,
    int ConnectedClients,
    string? LastError)
{
    public static readonly ListenerStatus Disabled = new(ListenerState.Disabled, 0, 0, null);
}

/// <summary>
/// Accepts RossTalk connections from ProPresenter and turns each command into a signal.
/// </summary>
/// <remarks>
/// ProPresenter's RossTalk device is a client: it dials out to an address and port you configure in
/// its Devices preferences, so this app is the server. A macro with a RossTalk action then reaches
/// this process directly — no MIDI ports, no bridging software, and it works across the network.
/// </remarks>
public sealed class RossTalkListener(
    ConfigurationStore config,
    SignalBus signals,
    ActivityLog activity,
    ILogger<RossTalkListener> logger) : BackgroundService
{
    private readonly List<string> _clients = [];
    private readonly Lock _clientGate = new();

    private volatile ListenerStatus _status = ListenerStatus.Disabled;
    private CancellationTokenSource? _restart;
    private string? _lastBindFailure;

    public ListenerStatus Status => _status;

    public event Action? StatusChanged;

    /// <summary>Drops the current socket and re-reads the port from configuration.</summary>
    public void Restart() => _restart?.Cancel();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        config.Changed += OnConfigChanged;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                _restart = linked;

                var settings = config.Current.Signals;

                if (!settings.RossTalkEnabled)
                {
                    SetStatus(ListenerStatus.Disabled);
                    await WaitForRestartAsync(linked.Token);
                    continue;
                }

                try
                {
                    await ListenAsync(settings.RossTalkPort, linked.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Deliberate restart after a settings change.
                }
                catch (SocketException ex)
                {
                    // Almost always "address already in use" — report it rather than retrying blindly.
                    // The retry runs every fifteen seconds for as long as the port stays taken, so the
                    // report is made once and then withheld until the port or the error changes.
                    var failure = $"{settings.RossTalkPort}: {ex.Message}";
                    if (Interlocked.Exchange(ref _lastBindFailure, failure) != failure)
                    {
                        logger.LogError(ex, "RossTalk listener could not bind port {Port}", settings.RossTalkPort);
                        activity.Error(
                            $"RossTalk listener could not start on port {settings.RossTalkPort}",
                            ex.Message);
                    }

                    SetStatus(new ListenerStatus(ListenerState.Failed, settings.RossTalkPort, 0, ex.Message));

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(15), linked.Token);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "RossTalk listener failed");
                    SetStatus(new ListenerStatus(ListenerState.Failed, settings.RossTalkPort, 0, ex.Message));
                }
                finally
                {
                    _restart = null;
                }
            }
        }
        finally
        {
            config.Changed -= OnConfigChanged;
            SetStatus(ListenerStatus.Disabled);
        }
    }

    private SignalSettings? _appliedSettings;

    private void OnConfigChanged(AppConfiguration configuration)
    {
        // Only re-dial when the listener settings actually changed; the config file is rewritten on
        // every rule edit and rebinding the socket each time would drop ProPresenter's connection.
        var settings = configuration.Signals;
        if (_appliedSettings is { } applied
            && applied.RossTalkEnabled == settings.RossTalkEnabled
            && applied.RossTalkPort == settings.RossTalkPort)
        {
            return;
        }

        Restart();
    }

    private async Task ListenAsync(int port, CancellationToken ct)
    {
        _appliedSettings = config.Current.Signals with { };

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        _lastBindFailure = null;
        SetStatus(new ListenerStatus(ListenerState.Listening, port, 0, null));
        logger.LogInformation("RossTalk listener started on port {Port}", port);
        activity.Info($"Listening for RossTalk on port {port}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
            }
        }
        finally
        {
            listener.Stop();
            lock (_clientGate) _clients.Clear();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        lock (_clientGate) _clients.Add(remote);
        UpdateClientCount();
        logger.LogInformation("RossTalk client connected from {Remote}", remote);

        try
        {
            using (client)
            {
                client.NoDelay = true;

                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);

                // ReadLineAsync handles both \n and \r\n terminators, which the protocol allows.
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break; // client closed

                    if (RossTalk.ParseSignalKey(line) is not { } key) continue;

                    signals.Publish(key, $"RossTalk from {remote}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "RossTalk client {Remote} ended", remote);
        }
        finally
        {
            lock (_clientGate) _clients.Remove(remote);
            UpdateClientCount();
            logger.LogInformation("RossTalk client {Remote} disconnected", remote);
        }
    }

    private void UpdateClientCount()
    {
        int count;
        lock (_clientGate) count = _clients.Count;

        if (_status.State == ListenerState.Listening)
        {
            SetStatus(_status with { ConnectedClients = count });
        }
    }

    private static async Task WaitForRestartAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetStatus(ListenerStatus status)
    {
        _status = status;
        try
        {
            StatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A listener status subscriber threw");
        }
    }
}

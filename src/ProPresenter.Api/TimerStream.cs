using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProPresenter.Api;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

public sealed record ConnectionStatus(
    ConnectionState State,
    DateTimeOffset? ConnectedSince,
    DateTimeOffset? LastUpdate,
    string? LastError,
    int ConsecutiveFailures)
{
    public static readonly ConnectionStatus Initial = new(ConnectionState.Disconnected, null, null, null, 0);
}

/// <summary>
/// Maintains a persistent chunked connection to <c>/v1/timers/current</c> and republishes each tick.
/// </summary>
/// <remarks>
/// ProPresenter pushes the complete timer array roughly once per second while anything is running,
/// as compact JSON documents separated by a blank line. This class owns reconnection: any drop,
/// error, or endpoint change results in a re-dial with capped exponential backoff, because a service
/// that silently stops receiving ticks would silently stop firing cues.
/// </remarks>
public sealed class TimerStream : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly ProPresenterEndpoint _endpoint;
    private readonly ILogger<TimerStream> _logger;

    private CancellationTokenSource? _reconnectSignal;
    private ConnectionStatus _status = ConnectionStatus.Initial;

    public TimerStream(HttpClient http, ProPresenterEndpoint endpoint, ILogger<TimerStream> logger)
    {
        _http = http;
        _endpoint = endpoint;
        _logger = logger;
        _endpoint.Changed += OnEndpointChanged;
    }

    /// <summary>Fired for every tick received from ProPresenter.</summary>
    public event Action<IReadOnlyList<TimerCurrent>>? TimersUpdated;

    public event Action<ConnectionStatus>? StatusChanged;

    public ConnectionStatus Status => _status;

    /// <summary>Forces the current connection to drop and immediately re-dial.</summary>
    public void Reconnect() => _reconnectSignal?.Cancel();

    private void OnEndpointChanged()
    {
        _logger.LogInformation("ProPresenter endpoint changed to {Uri}; reconnecting timer stream", _endpoint.BaseUri);
        Reconnect();
    }

    /// <summary>Runs until <paramref name="stoppingToken"/> is cancelled, reconnecting as needed.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _reconnectSignal = linked;

            SetStatus(_status with { State = ConnectionState.Connecting });

            try
            {
                await ConsumeAsync(linked.Token);

                // A clean end of stream still means we are no longer receiving ticks.
                failures = 0;
                SetStatus(_status with
                {
                    State = ConnectionState.Disconnected,
                    ConnectedSince = null,
                    LastError = "Stream closed by ProPresenter",
                });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // Deliberate reconnect (endpoint change or manual). Not a failure.
                failures = 0;
                SetStatus(_status with { State = ConnectionState.Disconnected, ConnectedSince = null });
            }
            catch (Exception ex)
            {
                failures++;

                // Only the first failure of an outage is worth a line, and only the message: the
                // stack trace is identical on every retry and says nothing the message does not.
                _logger.LogDebug(ex, "Timer stream to {Uri} failed", _endpoint.BaseUri);
                if (failures == 1)
                {
                    _logger.LogWarning("Timer stream to {Uri} failed — {Error}", _endpoint.BaseUri, ex.Message);
                }

                SetStatus(_status with
                {
                    State = ConnectionState.Disconnected,
                    ConnectedSince = null,
                    LastError = ex.Message,
                    ConsecutiveFailures = failures,
                });
            }
            finally
            {
                _reconnectSignal = null;
            }

            if (stoppingToken.IsCancellationRequested) break;

            var delay = failures == 0
                ? TimeSpan.FromMilliseconds(250)
                : TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(failures, 4)) / 2));

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetStatus(_status with { State = ConnectionState.Disconnected, ConnectedSince = null });
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        var uri = _endpoint.Resolve("v1/timers/current?chunked=true");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        SetStatus(_status with
        {
            State = ConnectionState.Connected,
            ConnectedSince = DateTimeOffset.Now,
            LastError = null,
            ConsecutiveFailures = 0,
        });
        _logger.LogInformation("Timer stream connected to {Uri}", uri);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var buffer = new StringBuilder();
        var chars = new char[4096];

        while (!ct.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(chars.AsMemory(), ct);
            if (read == 0) break; // server closed the connection

            buffer.Append(chars, 0, read);
            DrainCompleteDocuments(buffer);
        }
    }

    /// <summary>
    /// Pulls every complete JSON document out of the buffer, leaving any partial tail in place.
    /// </summary>
    /// <remarks>
    /// Documents are found by matching brackets rather than by splitting on the blank line that
    /// ProPresenter puts between them. The separator only appears <em>between</em> documents, so a
    /// delimiter-based split would strand the most recent one in the buffer until the next tick
    /// arrived — and when every timer is stopped, the next tick may never come.
    /// </remarks>
    private void DrainCompleteDocuments(StringBuilder buffer)
    {
        while (true)
        {
            var text = buffer.ToString();
            var end = FindDocumentEnd(text, out var start);
            if (end < 0)
            {
                // Discard leading whitespace so it cannot accumulate across ticks.
                if (start > 0) buffer.Remove(0, start);
                return;
            }

            Publish(text[start..end]);
            buffer.Remove(0, end);
        }
    }

    /// <summary>
    /// Returns the exclusive end index of the first complete JSON array or object in
    /// <paramref name="text"/>, or -1 if it is still incomplete. <paramref name="start"/> receives the
    /// index the document begins at.
    /// </summary>
    internal static int FindDocumentEnd(string text, out int start)
    {
        start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        if (start >= text.Length) return -1;

        var opener = text[start];
        if (opener is not ('[' or '{')) return -1;

        var closer = opener == '[' ? ']' : '}';
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == opener) depth++;
            else if (c == closer && --depth == 0) return i + 1;
        }

        return -1;
    }

    private void Publish(string document)
    {
        List<TimerCurrent>? timers;
        try
        {
            timers = JsonSerializer.Deserialize<List<TimerCurrent>>(document, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Skipping unparseable timer stream document");
            return;
        }

        if (timers is null) return;

        // Record the tick time without raising StatusChanged. Subscribers treat that event as a
        // connection-lifecycle signal; raising it once a second would make every tick look like a
        // fresh connection.
        _status = _status with { LastUpdate = DateTimeOffset.Now };

        try
        {
            TimersUpdated?.Invoke(timers);
        }
        catch (Exception ex)
        {
            // A misbehaving subscriber must not tear down the stream.
            _logger.LogError(ex, "A timer stream subscriber threw");
        }
    }

    private void SetStatus(ConnectionStatus status)
    {
        _status = status;
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A connection status subscriber threw");
        }
    }

    public void Dispose()
    {
        _endpoint.Changed -= OnEndpointChanged;
        _reconnectSignal?.Cancel();
    }
}

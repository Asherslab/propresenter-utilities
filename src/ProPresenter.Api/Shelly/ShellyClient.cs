using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProPresenter.Api.Shelly;

/// <summary>Result of switching one relay output.</summary>
public sealed record ShellySwitchResult(int Channel, bool Success, string? Error = null);

/// <summary>
/// Talks to a Shelly Gen2+ relay (Pro4PM and friends) over its RPC-over-HTTP API.
/// </summary>
/// <remarks>
/// Each output is one <c>Switch.Set</c> call; the device has no multi-output form. When the device has
/// authentication enabled it uses HTTP digest auth with SHA-256 and the fixed user name <c>admin</c>,
/// which <see cref="SocketsHttpHandler"/> negotiates on its own. The handler is rebuilt whenever the
/// host or password changes, so settings apply without a restart.
/// </remarks>
public sealed class ShellyClient(ILogger<ShellyClient> logger) : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();
    private HttpClient? _http;
    private string? _configuredFor;

    /// <summary>Sets every output in <paramref name="channels"/> concurrently.</summary>
    /// <param name="toggleAfter">
    /// When set, the device flips each output back by itself after this long. The timer runs on the
    /// relay, so it still happens if this app, the network or ProPresenter fails in between.
    /// </param>
    public async Task<IReadOnlyList<ShellySwitchResult>> SetAsync(
        string host,
        string? password,
        IReadOnlyList<int> channels,
        bool on,
        TimeSpan? toggleAfter = null,
        CancellationToken ct = default)
    {
        var http = ClientFor(host, password);
        var results = await Task.WhenAll(channels.Select(channel =>
            SetOneAsync(http, host, channel, on, toggleAfter, ct)));
        return results;
    }

    /// <summary>Reads each output's state. A channel that cannot be read maps to null.</summary>
    public async Task<IReadOnlyDictionary<int, bool?>> GetStatesAsync(
        string host,
        string? password,
        IReadOnlyList<int> channels,
        CancellationToken ct = default)
    {
        var http = ClientFor(host, password);
        var states = await Task.WhenAll(channels.Select(async channel =>
        {
            try
            {
                using var response = await http.GetAsync(BuildStatusUri(host, channel), ct);
                if (!response.IsSuccessStatusCode) return (channel, (bool?)null);

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                return (channel, document.RootElement.TryGetProperty("output", out var output)
                    ? output.GetBoolean()
                    : (bool?)null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Shelly status read for channel {Channel} failed", channel);
                return (channel, (bool?)null);
            }
        }));

        return states.ToDictionary(s => s.channel, s => s.Item2);
    }

    private async Task<ShellySwitchResult> SetOneAsync(
        HttpClient http,
        string host,
        int channel,
        bool on,
        TimeSpan? toggleAfter,
        CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(BuildSetUri(host, channel, on, toggleAfter), ct);
            if (response.IsSuccessStatusCode) return new ShellySwitchResult(channel, true);

            var error = response.StatusCode == HttpStatusCode.Unauthorized
                ? "401 Unauthorized — check the password under Settings → FOH lights"
                : $"{(int)response.StatusCode} {response.ReasonPhrase}";
            logger.LogWarning("Shelly Switch.Set on channel {Channel} returned {Error}", channel, error);
            return new ShellySwitchResult(channel, false, error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Shelly Switch.Set on channel {Channel} failed — {Error}", channel, ex.Message);
            return new ShellySwitchResult(channel, false, ex.Message);
        }
    }

    internal static Uri BuildSetUri(string host, int channel, bool on, TimeSpan? toggleAfter)
    {
        var query = $"id={channel}&on={(on ? "true" : "false")}";
        if (toggleAfter is { TotalSeconds: > 0 } after)
        {
            query += "&toggle_after=" + Math.Ceiling(after.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return new Uri(BaseUri(host), "rpc/Switch.Set?" + query);
    }

    internal static Uri BuildStatusUri(string host, int channel) =>
        new(BaseUri(host), $"rpc/Switch.GetStatus?id={channel}");

    /// <summary>Accepts a bare host or IP, with or without a scheme or port.</summary>
    internal static Uri BaseUri(string host)
    {
        var trimmed = host.Trim().TrimEnd('/');
        if (trimmed.Length == 0) throw new InvalidOperationException("No Shelly host is configured.");

        return new Uri(trimmed.Contains("://", StringComparison.Ordinal) ? trimmed + "/" : $"http://{trimmed}/");
    }

    private HttpClient ClientFor(string host, string? password)
    {
        var key = $"{host.Trim()}|{password}";
        lock (_gate)
        {
            if (_http is not null && _configuredFor == key) return _http;

            var handler = new SocketsHttpHandler();
            if (!string.IsNullOrEmpty(password))
            {
                handler.Credentials = new CredentialCache
                {
                    { BaseUri(host), "Digest", new NetworkCredential("admin", password) },
                };
            }

            // The old client may still have a request in flight; let it be collected rather than
            // disposing it underneath that request.
            _http = new HttpClient(handler) { Timeout = RequestTimeout };
            _configuredFor = key;
            return _http;
        }
    }

    public void Dispose() => _http?.Dispose();
}

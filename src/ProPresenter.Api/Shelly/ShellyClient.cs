using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ProPresenter.Api.Shelly;

/// <summary>Result of switching one relay output.</summary>
public sealed record ShellySwitchResult(int Channel, bool Success, string? Error = null);

/// <summary>
/// Talks to a Shelly Gen2+ relay (Pro4PM and friends) over its RPC-over-HTTP API.
/// </summary>
/// <remarks>
/// Several outputs are switched with one <c>Switch.SetMany</c> call, so they change together rather
/// than as separate requests that the device may serve one or two at a time. Firmware without that
/// method gets one <c>Switch.Set</c> per output instead. When the device has authentication enabled it uses HTTP digest auth with SHA-256 and the fixed user name <c>admin</c>,
/// which <see cref="SocketsHttpHandler"/> negotiates on its own. The handler is rebuilt whenever the
/// host or password changes, so settings apply without a restart.
/// </remarks>
public sealed class ShellyClient(ILogger<ShellyClient> logger) : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();
    private HttpClient? _http;
    private string? _configuredFor;

    /// <summary>The device (host and password) last found not to support Switch.SetMany.</summary>
    private string? _noSetManyFor;

    /// <summary>Sets every output in <paramref name="channels"/>, together where the firmware allows.</summary>
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
        var key = KeyFor(host, password);

        if (channels.Count > 1 && Volatile.Read(ref _noSetManyFor) != key)
        {
            var many = await SetManyAsync(http, host, channels, on, toggleAfter, ct);
            if (many is not null) return many;

            Volatile.Write(ref _noSetManyFor, key);
            logger.LogWarning(
                "Shelly at {Host} has no Switch.SetMany (older firmware?); switching outputs one request each", host);
        }

        var results = await Task.WhenAll(channels.Select(channel =>
            SetOneAsync(http, host, channel, on, toggleAfter, ct)));
        return results;
    }

    /// <returns>Per-output results, or null when the firmware does not have Switch.SetMany.</returns>
    private async Task<IReadOnlyList<ShellySwitchResult>?> SetManyAsync(
        HttpClient http,
        string host,
        IReadOnlyList<int> channels,
        bool on,
        TimeSpan? toggleAfter,
        CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(BuildSetManyRequest(channels, on, toggleAfter), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(new Uri(BaseUri(host), "rpc"), content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return Failed(channels, "401 Unauthorized — check the password under Settings → FOH lights");
            }

            return ParseSetManyResponse(channels, (int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Unreachable is not "unsupported": report it rather than retrying each output, which
            // would only fail the same way four times.
            logger.LogWarning("Shelly Switch.SetMany failed — {Error}", ex.Message);
            return Failed(channels, ex.Message);
        }
    }

    internal static string BuildSetManyRequest(IReadOnlyList<int> channels, bool on, TimeSpan? toggleAfter)
    {
        var parameters = new JsonObject
        {
            ["ids"] = new JsonArray(channels.Select(c => (JsonNode)c).ToArray()),
            ["on"] = on,
        };
        if (toggleAfter is { TotalSeconds: > 0 } after) parameters["toggle_after"] = Math.Ceiling(after.TotalSeconds);

        return new JsonObject { ["id"] = 1, ["method"] = "Switch.SetMany", ["params"] = parameters }.ToJsonString();
    }

    /// <summary>
    /// Maps a JSON-RPC answer to per-output results. A top-level error means the call itself was
    /// refused, which on firmware without the method is "no handler"; that returns null so the caller
    /// falls back to Switch.Set.
    /// </summary>
    internal static IReadOnlyList<ShellySwitchResult>? ParseSetManyResponse(
        IReadOnlyList<int> channels,
        int status,
        string body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            root = null;
        }

        if (root?["result"]?["results"] is JsonArray results)
        {
            return channels.Select((channel, i) =>
            {
                var entry = i < results.Count ? results[i] : null;
                return entry is null
                    ? new ShellySwitchResult(channel, true)
                    : new ShellySwitchResult(channel, false, entry["message"]?.GetValue<string>() ?? entry.ToJsonString());
            }).ToList();
        }

        // Any other answer, a 404 or an error object, is treated as the method being unavailable.
        return status is >= 200 and < 300 && root?["result"] is not null && root["error"] is null
            ? channels.Select(channel => new ShellySwitchResult(channel, true)).ToList()
            : null;
    }

    private static IReadOnlyList<ShellySwitchResult> Failed(IReadOnlyList<int> channels, string error) =>
        channels.Select(channel => new ShellySwitchResult(channel, false, error)).ToList();

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
        var key = KeyFor(host, password);
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

    private static string KeyFor(string host, string? password) => $"{host.Trim()}|{password}";

    public void Dispose() => _http?.Dispose();
}

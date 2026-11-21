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
/// <para>
/// Separate requests per output change visibly out of step: the device serves them a couple at a
/// time. So several outputs are switched in one request, by the first of these that works:
/// <c>Switch.SetMany</c> where the firmware has it; otherwise a small script this client installs on
/// the device (<see cref="ScriptName"/>), which calls <c>Switch.Set</c> for each output from inside
/// the device; and only failing both, one <c>Switch.Set</c> request per output.
/// </para>
/// When the device has authentication enabled it uses HTTP digest auth with SHA-256 and the fixed
/// user name <c>admin</c>, which <see cref="SocketsHttpHandler"/> negotiates on its own. The handler
/// is rebuilt whenever the host or password changes, so settings apply without a restart.
/// </remarks>
public sealed class ShellyClient(ILogger<ShellyClient> logger) : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();
    private HttpClient? _http;
    private string? _configuredFor;

    /// <summary>The device (host and password) last found not to support Switch.SetMany.</summary>
    private string? _noSetManyFor;

    /// <summary>The name the script is installed under, and how it is found again.</summary>
    public const string ScriptName = "propresenter-utilities";

    /// <summary>
    /// First line of the current script. A device whose copy lacks it gets the new code; bump the
    /// version in the script whenever its behaviour changes.
    /// </summary>
    internal const string ScriptMarker = "// propresenter-utilities v1:";

    internal static readonly string ScriptCode = LoadScript();

    private readonly SemaphoreSlim _scriptGate = new(1, 1);
    private string? _scriptFor;
    private int _scriptId;

    /// <summary>The device last found unable to run the script, so it is not retried on every switch.</summary>
    private string? _noScriptFor;

    /// <summary>Sets every output in <paramref name="channels"/>, together where the firmware allows.</summary>
    /// <param name="toggleAfter">
    /// When set, the device flips each output back by itself after this long. The timer runs on the
    /// relay, so it still happens if this app, the network or ProPresenter fails in between.
    /// </param>
    /// <param name="useScript">Whether a script may be installed on the device to switch outputs together.</param>
    public async Task<IReadOnlyList<ShellySwitchResult>> SetAsync(
        string host,
        string? password,
        IReadOnlyList<int> channels,
        bool on,
        TimeSpan? toggleAfter = null,
        bool useScript = true,
        CancellationToken ct = default)
    {
        var http = ClientFor(host, password);
        var key = KeyFor(host, password);

        if (channels.Count > 1 && Volatile.Read(ref _noSetManyFor) != key)
        {
            var many = await SetManyAsync(http, host, channels, on, toggleAfter, ct);
            if (many is not null) return many;

            Volatile.Write(ref _noSetManyFor, key);
            logger.LogInformation("Shelly at {Host} has no Switch.SetMany", host);
        }

        if (channels.Count > 1 && useScript && Volatile.Read(ref _noScriptFor) != key)
        {
            var scripted = await SetViaScriptAsync(http, host, key, channels, on, toggleAfter, ct);
            if (scripted is not null) return scripted;

            Volatile.Write(ref _noScriptFor, key);
            logger.LogWarning(
                "Shelly at {Host} cannot run the {Script} script; switching outputs one request each, which may not be simultaneous",
                host, ScriptName);
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

    // -------------------------------------------------------------- script

    /// <returns>Per-output results, or null when the script cannot be used on this device.</returns>
    private async Task<IReadOnlyList<ShellySwitchResult>?> SetViaScriptAsync(
        HttpClient http,
        string host,
        string key,
        IReadOnlyList<int> channels,
        bool on,
        TimeSpan? toggleAfter,
        CancellationToken ct)
    {
        // A second attempt covers a script stopped or deleted on the device since it was installed.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            int id;
            try
            {
                if (await EnsureScriptAsync(http, host, key, ct) is not { } installed) return null;
                id = installed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Unreachable or timed out while setting up: report it, as a per-output retry would
                // fail the same way.
                return Failed(channels, ex.Message);
            }

            try
            {
                using var response = await http.GetAsync(BuildScriptUri(host, id, channels, on, toggleAfter), ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return Failed(channels, "401 Unauthorized — check the password under Settings → FOH lights");
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    ForgetScript(key);
                    continue;
                }

                return ParseScriptResponse(channels, body)
                    ?? Failed(channels, $"{(int)response.StatusCode} {response.ReasonPhrase} from the {ScriptName} script");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Unreachable: switching each output separately would fail the same way.
                logger.LogWarning("Shelly script switch failed — {Error}", ex.Message);
                return Failed(channels, ex.Message);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the script on the device by name, installing it or replacing older code as needed, and
    /// makes sure it is running and starts at boot.
    /// </summary>
    /// <returns>The script's id, or null when the device will not run it.</returns>
    /// <exception cref="HttpRequestException">The device could not be reached.</exception>
    private async Task<int?> EnsureScriptAsync(HttpClient http, string host, string key, CancellationToken ct)
    {
        await _scriptGate.WaitAsync(ct);
        try
        {
            if (_scriptFor == key) return _scriptId;

            var list = await RpcAsync(http, host, "Script.List", null, ct);
            var existing = (list?["scripts"] as JsonArray ?? [])
                .FirstOrDefault(s => s?["name"]?.GetValue<string>() == ScriptName);

            int id;
            bool running;
            var current = false;

            if (existing is null)
            {
                id = (await RpcAsync(http, host, "Script.Create", new JsonObject { ["name"] = ScriptName }, ct))!["id"]!.GetValue<int>();
                running = false;
            }
            else
            {
                id = existing["id"]!.GetValue<int>();
                running = existing["running"]?.GetValue<bool>() ?? false;
                var head = await RpcAsync(http, host, "Script.GetCode",
                    new JsonObject { ["id"] = id, ["offset"] = 0, ["len"] = ScriptMarker.Length }, ct);
                current = head?["data"]?.GetValue<string>() == ScriptMarker;
            }

            if (!current)
            {
                // Code can only be replaced while the script is stopped.
                if (running) await RpcAsync(http, host, "Script.Stop", new JsonObject { ["id"] = id }, ct);
                running = false;

                foreach (var (chunk, i) in Chunks(ScriptCode).Select((c, i) => (c, i)))
                {
                    await RpcAsync(http, host, "Script.PutCode",
                        new JsonObject { ["id"] = id, ["code"] = chunk, ["append"] = i > 0 }, ct);
                }

                logger.LogInformation("Installed the {Script} script on the Shelly at {Host} (script {Id})", ScriptName, host, id);
            }

            await RpcAsync(http, host, "Script.SetConfig",
                new JsonObject { ["id"] = id, ["config"] = new JsonObject { ["enable"] = true } }, ct);
            if (!running) await RpcAsync(http, host, "Script.Start", new JsonObject { ["id"] = id }, ct);

            _scriptFor = key;
            _scriptId = id;
            return id;
        }
        catch (ShellyRpcException ex)
        {
            logger.LogWarning("Could not set up the {Script} script on the Shelly at {Host} — {Error}", ScriptName, host, ex.Message);
            return null;
        }
        finally
        {
            _scriptGate.Release();
        }
    }

    private void ForgetScript(string key)
    {
        _scriptGate.Wait();
        try
        {
            if (_scriptFor == key) _scriptFor = null;
        }
        finally
        {
            _scriptGate.Release();
        }
    }

    /// <summary>One JSON-RPC call. The device's answer to a refused call becomes a <see cref="ShellyRpcException"/>.</summary>
    private static async Task<JsonNode?> RpcAsync(
        HttpClient http,
        string host,
        string method,
        JsonObject? parameters,
        CancellationToken ct)
    {
        var request = new JsonObject { ["id"] = 1, ["method"] = method };
        if (parameters is not null) request["params"] = parameters;

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(new Uri(BaseUri(host), "rpc"), content, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ShellyRpcException($"{method}: 401 Unauthorized — check the password under Settings → FOH lights");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            throw new ShellyRpcException($"{method}: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        if (root?["error"] is { } error)
        {
            throw new ShellyRpcException($"{method}: {error["message"]?.GetValue<string>() ?? error.ToJsonString()}");
        }

        return root?["result"];
    }

    internal static Uri BuildScriptUri(string host, int scriptId, IReadOnlyList<int> channels, bool on, TimeSpan? toggleAfter)
    {
        var query = $"ids={string.Join(",", channels.Select(c => c.ToString(CultureInfo.InvariantCulture)))}&on={(on ? "true" : "false")}";
        if (toggleAfter is { TotalSeconds: > 0 } after)
        {
            query += "&toggle_after=" + Math.Ceiling(after.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return new Uri(BaseUri(host), $"script/{scriptId}/set?{query}");
    }

    /// <summary>The script answers <c>{"results":[null | "error", ...]}</c>, one entry per output in order.</summary>
    internal static IReadOnlyList<ShellySwitchResult>? ParseScriptResponse(IReadOnlyList<int> channels, string body)
    {
        try
        {
            if (JsonNode.Parse(body)?["results"] is not JsonArray results) return null;

            return channels.Select((channel, i) =>
            {
                var entry = i < results.Count ? results[i] : null;
                return entry is null
                    ? new ShellySwitchResult(channel, true)
                    : new ShellySwitchResult(channel, false, entry.ToString());
            }).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Shelly's own tooling uploads scripts in chunks of about a kilobyte.</summary>
    internal static IEnumerable<string> Chunks(string code, int size = 1000)
    {
        for (var i = 0; i < code.Length; i += size) yield return code.Substring(i, Math.Min(size, code.Length - i));
    }

    private static string LoadScript()
    {
        using var stream = typeof(ShellyClient).Assembly.GetManifestResourceStream("ShellyScript.js")
            ?? throw new InvalidOperationException("The Shelly script resource is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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

    public void Dispose()
    {
        _http?.Dispose();
        _scriptGate.Dispose();
    }
}

/// <summary>The device refused an RPC call (its <c>error</c> member), or answered with something unreadable.</summary>
public sealed class ShellyRpcException(string message) : Exception(message);

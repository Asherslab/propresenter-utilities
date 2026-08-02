using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProPresenter.Api;

/// <summary>
/// Typed access to the ProPresenter HTTP API.
/// </summary>
/// <remarks>
/// The API is unauthenticated (the published OpenAPI document declares no security schemes), so the
/// only connection state is host and port, held by <see cref="ProPresenterEndpoint"/>. Every request
/// resolves its absolute URI at call time so the endpoint can change without recreating the client.
/// </remarks>
public sealed class ProPresenterClient(
    HttpClient http,
    ProPresenterEndpoint endpoint,
    ILogger<ProPresenterClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string? _lastFailure;

    public ProPresenterEndpoint Endpoint => endpoint;

    // ---------------------------------------------------------------- core

    /// <summary>
    /// Issues an arbitrary API request. This is what the action executor uses, so every catalog entry
    /// funnels through one place for logging and error handling.
    /// </summary>
    public async Task<ApiResult> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken ct = default)
    {
        var uri = endpoint.Resolve(path);
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            var payload = response.Content.Headers.ContentLength is 0
                ? string.Empty
                : await response.Content.ReadAsStringAsync(ct);

            // Whatever it answered, it answered: any earlier outage is over.
            NoteReachable();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("ProPresenter {Method} {Path} returned {Status}", method, path, (int)response.StatusCode);
                return ApiResult.Failure($"{(int)response.StatusCode} {response.ReasonPhrase}", response.StatusCode, payload);
            }

            return ApiResult.Ok(payload, response.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            NoteFailure(method, path, ex);
            return ApiResult.Failure(ex.Message, null, null);
        }
    }

    /// <summary>
    /// Reports a transport failure once, then stays quiet until something changes.
    /// </summary>
    /// <remarks>
    /// ProPresenter being closed, or configured on the wrong port, is a normal and often prolonged
    /// state. Background loops keep polling throughout, so logging every attempt — with a stack trace
    /// that only ever says "connection refused" — drowns out anything useful. The first occurrence and
    /// the recovery are the two events an operator actually needs; the detail stays at Debug.
    /// </remarks>
    private void NoteFailure(HttpMethod method, string path, Exception ex)
    {
        logger.LogDebug(ex, "ProPresenter {Method} {Path} failed", method, path);

        var summary = $"{endpoint.BaseUri.Authority}: {ex.Message}";
        if (Interlocked.Exchange(ref _lastFailure, summary) == summary) return;

        logger.LogWarning("ProPresenter {Method} {Path} failed — {Error}", method, path, ex.Message);
    }

    private void NoteReachable()
    {
        if (Interlocked.Exchange(ref _lastFailure, null) is not null)
        {
            logger.LogInformation("ProPresenter at {Uri} is responding again", endpoint.BaseUri);
        }
    }

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        var result = await SendAsync(HttpMethod.Get, path, body: null, ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Body)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(result.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not deserialize response from {Path}", path);
            return default;
        }
    }

    // ------------------------------------------------------------- version

    public Task<VersionInfo?> GetVersionAsync(CancellationToken ct = default) =>
        GetJsonAsync<VersionInfo>("version", ct);

    /// <summary>Cheap reachability probe used by the connection monitor.</summary>
    public async Task<bool> PingAsync(CancellationToken ct = default) =>
        await GetVersionAsync(ct) is not null;

    // -------------------------------------------------------------- timers

    public async Task<IReadOnlyList<TimerConfiguration>> GetTimersAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<TimerConfiguration>>("v1/timers", ct) ?? [];

    public async Task<IReadOnlyList<TimerCurrent>> GetCurrentTimersAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<TimerCurrent>>("v1/timers/current", ct) ?? [];

    public Task<ApiResult> TimerOperationAsync(string timerId, TimerOperation operation, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"v1/timer/{Escape(timerId)}/{operation.ToString().ToLowerInvariant()}", body: null, ct);

    public Task<ApiResult> AllTimersOperationAsync(TimerOperation operation, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"v1/timers/{operation.ToString().ToLowerInvariant()}", body: null, ct);

    /// <summary>Adds (or, with a negative value, removes) seconds from a running timer.</summary>
    public Task<ApiResult> IncrementTimerAsync(string timerId, int seconds, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"v1/timer/{Escape(timerId)}/increment/{seconds}", body: null, ct);

    public Task<ApiResult> CreateTimerAsync(object configuration, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "v1/timers", configuration, ct);

    public Task<ApiResult> UpdateTimerAsync(string timerId, object configuration, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"v1/timer/{Escape(timerId)}", configuration, ct);

    public Task<ApiResult> DeleteTimerAsync(string timerId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"v1/timer/{Escape(timerId)}", body: null, ct);

    // ----------------------------------------------------- pickers / lists

    public async Task<IReadOnlyList<MacroInfo>> GetMacrosAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<MacroInfo>>("v1/macros", ct) ?? [];

    public async Task<IReadOnlyList<PlaylistInfo>> GetPresentationPlaylistsAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<PlaylistInfo>>("v1/playlists", ct) ?? [];

    public async Task<IReadOnlyList<PlaylistInfo>> GetAudioPlaylistsAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<PlaylistInfo>>("v1/audio/playlists", ct) ?? [];

    public async Task<IReadOnlyList<PlaylistInfo>> GetMediaPlaylistsAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<PlaylistInfo>>("v1/media/playlists", ct) ?? [];

    public async Task<IReadOnlyList<NamedItem>> GetLooksAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<NamedItem>>("v1/looks", ct) ?? [];

    public async Task<IReadOnlyList<NamedItem>> GetPropsAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<NamedItem>>("v1/props", ct) ?? [];

    public async Task<IReadOnlyList<NamedItem>> GetClearGroupsAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<NamedItem>>("v1/clear/groups", ct) ?? [];

    public async Task<IReadOnlyList<NamedItem>> GetMessagesAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<NamedItem>>("v1/messages", ct) ?? [];

    public async Task<IReadOnlyList<NamedItem>> GetStageLayoutsAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<NamedItem>>("v1/stage/layouts", ct) ?? [];

    public async Task<IReadOnlyList<NamedItem>> GetStageScreensAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<NamedItem>>("v1/stage/screens", ct) ?? [];

    public async Task<IReadOnlyList<NamedItem>> GetVideoInputsAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<NamedItem>>("v1/video_inputs", ct) ?? [];

    public async Task<IReadOnlyList<NamedItem>> GetGlobalGroupsAsync(CancellationToken ct = default) =>
        await GetJsonAsync<List<NamedItem>>("v1/groups", ct) ?? [];

    private static string Escape(string value) => Uri.EscapeDataString(value);
}

public sealed record ApiResult(bool Success, string? Body, HttpStatusCode? StatusCode, string? Error)
{
    public static ApiResult Ok(string? body, HttpStatusCode status) => new(true, body, status, null);

    public static ApiResult Failure(string error, HttpStatusCode? status, string? body) =>
        new(false, body, status, error);
}

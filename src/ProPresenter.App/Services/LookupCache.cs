using ProPresenter.Api;
using ProPresenter.Api.Actions;

namespace ProPresenter.App.Services;

/// <summary>One selectable value in an action parameter dropdown.</summary>
public sealed record LookupOption(string Value, string Label);

/// <summary>
/// Caches the ProPresenter lists that back the action pickers (macros, playlists, looks and so on).
/// </summary>
/// <remarks>
/// These lists change while the operator is building macros in ProPresenter and switching back and
/// forth, so a long cache makes the editor feel stale. They also change almost never during a live
/// service, when polling would be pure noise on the machine driving the screens. Refresh cadence
/// therefore follows <see cref="UiActivity"/>: quick while someone is editing, slow when idle.
/// </remarks>
public sealed class LookupCache(ProPresenterClient client, UiActivity activity, ILogger<LookupCache> logger)
{
    /// <summary>How stale a list may be before a refresh is triggered while the operator is active.</summary>
    public static readonly TimeSpan ActiveTtl = TimeSpan.FromSeconds(5);

    /// <summary>How stale a list may be before a refresh is triggered while nobody is using the UI.</summary>
    public static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(10);

    private readonly Dictionary<ParameterKind, Entry> _cache = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action? Changed;

    private sealed record Entry(IReadOnlyList<LookupOption> Options, DateTimeOffset LoadedAt);

    public IReadOnlyList<LookupOption> Get(ParameterKind kind) =>
        _cache.TryGetValue(kind, out var entry) ? entry.Options : [];

    public bool IsLoaded(ParameterKind kind) => _cache.ContainsKey(kind);

    /// <summary>Kinds that have been asked for at least once, and so are worth keeping warm.</summary>
    public IReadOnlyCollection<ParameterKind> TrackedKinds => _cache.Keys.ToList();

    /// <summary>
    /// Loads a lookup if it is missing or stale. Returns immediately when the cached copy is fresh
    /// enough for the current activity level.
    /// </summary>
    public async Task EnsureLoadedAsync(ParameterKind kind, CancellationToken ct = default)
    {
        if (!IsStale(kind)) return;
        await LoadAsync(kind, force: true, ct);
    }

    private bool IsStale(ParameterKind kind)
    {
        if (!_cache.TryGetValue(kind, out var entry)) return true;

        var ttl = activity.IsActive ? ActiveTtl : IdleTtl;
        return DateTimeOffset.UtcNow - entry.LoadedAt > ttl;
    }

    /// <summary>Refreshes every lookup that has been used, regardless of age. Backs the manual button.</summary>
    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        foreach (var kind in TrackedKinds) await LoadAsync(kind, force: true, ct);
        Changed?.Invoke();
    }

    /// <summary>
    /// Refreshes only what has gone stale. Called on a timer by <see cref="LookupRefreshService"/>.
    /// </summary>
    public async Task RefreshStaleAsync(CancellationToken ct = default)
    {
        var refreshed = false;
        foreach (var kind in TrackedKinds)
        {
            if (!IsStale(kind)) continue;
            await LoadAsync(kind, force: true, ct);
            refreshed = true;
        }

        if (refreshed) Changed?.Invoke();
    }

    private async Task LoadAsync(ParameterKind kind, bool force, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed this while we waited for the gate.
            if (!force && !IsStale(kind)) return;

            var options = await FetchAsync(kind, ct);

            // Keep the previous list when a refresh comes back empty because ProPresenter is
            // unreachable; dropping the options would empty every dropdown mid-edit.
            if (options.Count == 0 && _cache.TryGetValue(kind, out var existing) && existing.Options.Count > 0)
            {
                _cache[kind] = existing with { LoadedAt = DateTimeOffset.UtcNow };
                return;
            }

            _cache[kind] = new Entry(options, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load {Kind} options", kind);
            _cache[kind] = new Entry(Get(kind), DateTimeOffset.UtcNow);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
    }

    private async Task<IReadOnlyList<LookupOption>> FetchAsync(ParameterKind kind, CancellationToken ct) => kind switch
    {
        ParameterKind.MacroId => ToOptions(await client.GetMacrosAsync(ct), m => m.Id),
        ParameterKind.TimerId => ToOptions(await client.GetTimersAsync(ct), t => t.Id),
        ParameterKind.PresentationPlaylistId => Flatten(await client.GetPresentationPlaylistsAsync(ct)),
        ParameterKind.AudioPlaylistId => Flatten(await client.GetAudioPlaylistsAsync(ct)),
        ParameterKind.MediaPlaylistId => Flatten(await client.GetMediaPlaylistsAsync(ct)),
        ParameterKind.LookId => ToOptions(await client.GetLooksAsync(ct), l => l.Id),
        ParameterKind.PropId => ToOptions(await client.GetPropsAsync(ct), p => p.Id),
        ParameterKind.ClearGroupId => ToOptions(await client.GetClearGroupsAsync(ct), c => c.Id),
        ParameterKind.MessageId => ToOptions(await client.GetMessagesAsync(ct), m => m.Id),
        ParameterKind.StageLayoutId => ToOptions(await client.GetStageLayoutsAsync(ct), s => s.Id),
        ParameterKind.StageScreenId => ToOptions(await client.GetStageScreensAsync(ct), s => s.Id),
        ParameterKind.VideoInputId => ToOptions(await client.GetVideoInputsAsync(ct), v => v.Id),
        ParameterKind.GroupId => ToOptions(await client.GetGlobalGroupsAsync(ct), g => g.Id),
        _ => [],
    };

    private static IReadOnlyList<LookupOption> ToOptions<T>(IEnumerable<T> items, Func<T, ProId> selector) =>
        items.Select(item => Option(selector(item))).Where(o => o is not null).Select(o => o!).ToList();

    /// <summary>Playlists nest into folders, so the tree is flattened with a path-style label.</summary>
    private static IReadOnlyList<LookupOption> Flatten(IEnumerable<PlaylistInfo> playlists)
    {
        var options = new List<LookupOption>();

        void Walk(IEnumerable<PlaylistInfo> nodes, string prefix)
        {
            foreach (var node in nodes)
            {
                var label = string.IsNullOrEmpty(prefix) ? node.Id.ToString() : $"{prefix} / {node.Id}";

                // Folders exist to hold other playlists and cannot themselves be triggered.
                var isFolder = string.Equals(node.FieldType, "group", StringComparison.OrdinalIgnoreCase)
                    || node.Children is { Count: > 0 };

                if (!isFolder && Option(node.Id, label) is { } option) options.Add(option);
                if (node.Children is { Count: > 0 }) Walk(node.Children, label);
            }
        }

        Walk(playlists, string.Empty);
        return options;
    }

    private static LookupOption? Option(ProId id, string? label = null)
    {
        // Prefer the UUID as the stored value: names change, and a renamed macro should not silently
        // stop a Sunday-morning cue from firing.
        var value = id.Uuid ?? id.Name ?? id.Index?.ToString();
        return value is null ? null : new LookupOption(value, label ?? id.ToString());
    }
}

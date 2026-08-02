using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ProPresenter.Triggers.Model;

namespace ProPresenter.Triggers;

/// <summary>
/// Reads and writes the JSON configuration file that holds profiles, rules and connection settings.
/// </summary>
/// <remarks>
/// Writes are atomic (temp file plus move) because this process is expected to run as a background
/// service and may be killed at any moment — a half-written profile file would lose a service's worth
/// of cue configuration.
/// </remarks>
public sealed class ConfigurationStore
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<ConfigurationStore> _logger;
    private readonly Lock _gate = new();
    private AppConfiguration _current = AppConfiguration.CreateDefault();

    public ConfigurationStore(ILogger<ConfigurationStore> logger, string? path = null)
    {
        _logger = logger;
        FilePath = path ?? ResolveDefaultPath();
        Load();
    }

    public string FilePath { get; }

    /// <summary>Raised after any successful save, so the engine and UI can pick up changes.</summary>
    public event Action<AppConfiguration>? Changed;

    public AppConfiguration Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>Applies a change and persists it. The mutation runs under the store's lock.</summary>
    public AppConfiguration Update(Func<AppConfiguration, AppConfiguration> mutate)
    {
        AppConfiguration updated;
        lock (_gate)
        {
            updated = mutate(_current);
            _current = updated;
            Save(updated);
        }

        Changed?.Invoke(updated);
        return updated;
    }

    /// <summary>Replaces the whole configuration, e.g. on import.</summary>
    public AppConfiguration Replace(AppConfiguration configuration) => Update(_ => configuration);

    /// <summary>
    /// Persists the current object graph. The editor binds directly to the live profile objects, so
    /// this is how it commits an in-place edit.
    /// </summary>
    public AppConfiguration Save() => Update(current => current);

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _logger.LogInformation("No configuration at {Path}; creating defaults", FilePath);
                Save(_current);
                return;
            }

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppConfiguration>(json, SerializerOptions);
            if (loaded is null)
            {
                _logger.LogWarning("Configuration at {Path} was empty; using defaults", FilePath);
                return;
            }

            // A file with no profiles would leave the UI with nothing to edit.
            if (loaded.Profiles.Count == 0)
            {
                var seed = new TriggerProfile { Name = "Default" };
                loaded = loaded with { Profiles = [seed], ActiveProfileId = seed.Id };
            }
            else if (loaded.ActiveProfile is null)
            {
                loaded = loaded with { ActiveProfileId = loaded.Profiles[0].Id };
            }

            _current = loaded;
            _logger.LogInformation("Loaded configuration from {Path}", FilePath);
        }
        catch (Exception ex)
        {
            // Keep running on defaults rather than refusing to start; the operator can fix the file
            // and reload from the UI.
            _logger.LogError(ex, "Could not read configuration at {Path}; continuing with defaults", FilePath);
        }
    }

    private void Save(AppConfiguration configuration)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(configuration, SerializerOptions);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write configuration to {Path}", FilePath);
        }
    }

    /// <summary>
    /// Per-user configuration directory. <c>%APPDATA%</c> on Windows, <c>$XDG_CONFIG_HOME</c> or
    /// <c>~/.config</c> elsewhere. Overridable so a service can point at a shared location.
    /// </summary>
    public static string ResolveDefaultPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("PROPRESENTER_UTILITIES_CONFIG");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(root, "propresenter-utilities", "config.json");
    }
}

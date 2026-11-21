namespace ProPresenter.Triggers.Model;

/// <summary>A named set of trigger rules. Exactly one profile is active at a time.</summary>
public sealed record TriggerProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "New profile";
    public string Notes { get; set; } = string.Empty;
    public List<TriggerRule> Rules { get; set; } = [];
}

public sealed record ConnectionSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 54937;
}

/// <summary>
/// Inbound signal listeners — how a ProPresenter macro reaches this app.
/// </summary>
public sealed record SignalSettings
{
    /// <summary>
    /// Listen for RossTalk commands. ProPresenter connects out to this port and sends a short ASCII
    /// command when a macro runs, which is what lets a macro fire the same rules a timer can.
    /// </summary>
    public bool RossTalkEnabled { get; set; } = true;

    /// <summary>7788 is the RossTalk convention, and what ProPresenter's device dialog defaults to.</summary>
    public int RossTalkPort { get; set; } = 7788;
}

/// <summary>
/// The Shelly relay that switches the FOH lights. Driven by the <c>lights.*</c> actions.
/// </summary>
public sealed record LightsSettings
{
    public bool Enabled { get; set; }

    /// <summary>IP address or host name of the Shelly, e.g. <c>192.168.1.50</c>.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Only needed when authentication is enabled on the device. Stored in plain text in the
    /// configuration file, like the rest of this app's settings.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>The relay outputs an action set to "all" switches. A Pro4PM has 0–3.</summary>
    public string Channels { get; set; } = "0,1,2,3";

    /// <summary>
    /// Install a small script on the Shelly so several outputs switch from one request, together.
    /// Without it each output is a separate request, and they change visibly out of step.
    /// </summary>
    public bool SyncWithScript { get; set; } = true;
}

/// <summary>Root of the on-disk configuration file.</summary>
public sealed record AppConfiguration
{
    /// <summary>Bumped only when a migration is needed; readers tolerate older values.</summary>
    public int Version { get; init; } = 1;

    public ConnectionSettings Connection { get; init; } = new();

    public SignalSettings Signals { get; init; } = new();

    public LightsSettings Lights { get; init; } = new();

    public List<TriggerProfile> Profiles { get; init; } = [];

    public Guid? ActiveProfileId { get; init; }

    /// <summary>
    /// Master switch. When false the engine keeps watching timers and updating the dashboard but
    /// fires nothing — useful for rehearsing without cueing audio.
    /// </summary>
    public bool TriggersArmed { get; init; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public TriggerProfile? ActiveProfile =>
        ActiveProfileId is { } id ? Profiles.FirstOrDefault(p => p.Id == id) : null;

    public static AppConfiguration CreateDefault()
    {
        var profile = new TriggerProfile { Name = "Default" };
        return new AppConfiguration
        {
            Profiles = [profile],
            ActiveProfileId = profile.Id,
        };
    }
}

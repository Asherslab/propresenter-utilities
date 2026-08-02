using System.Text.Json.Serialization;

namespace ProPresenter.Api;

/// <summary>
/// ProPresenter's common identifier shape. Most objects can be addressed by name, index or uuid.
/// </summary>
public sealed record ProId
{
    [JsonPropertyName("uuid")] public string? Uuid { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("index")] public int? Index { get; init; }

    /// <summary>Best available path segment for addressing this object. UUID is preferred: it survives renames.</summary>
    public string ToRouteValue() =>
        !string.IsNullOrWhiteSpace(Uuid) ? Uuid! :
        !string.IsNullOrWhiteSpace(Name) ? Name! :
        Index?.ToString() ?? throw new InvalidOperationException("ProId has no usable identifier.");

    public override string ToString() => Name ?? Uuid ?? Index?.ToString() ?? "(unknown)";
}

public enum TimerState
{
    Stopped,
    Running,
    Complete,
    Overrunning,
    Overran,
}

/// <summary>A single entry from <c>/v1/timers/current</c>.</summary>
public sealed record TimerCurrent
{
    [JsonPropertyName("id")] public required ProId Id { get; init; }

    /// <summary>Raw clock string as reported, e.g. <c>00:05:00</c> or <c>-00:00:02</c>.</summary>
    [JsonPropertyName("time")] public string Time { get; init; } = "00:00:00";

    [JsonPropertyName("state")] public string StateRaw { get; init; } = "stopped";

    // Derived views of the two fields above. Both must be ignored: under the web naming policy
    // 'State' would otherwise map onto the same "state" property as StateRaw and fail to serialize.
    [JsonIgnore] public TimeSpan Value => TimeCode.Parse(Time);

    [JsonIgnore]
    public TimerState State => StateRaw.ToLowerInvariant() switch
    {
        "running" => TimerState.Running,
        "complete" => TimerState.Complete,
        "overrunning" => TimerState.Overrunning,
        "overran" => TimerState.Overran,
        _ => TimerState.Stopped,
    };
}

public sealed record CountdownConfig
{
    /// <summary>Total duration in seconds.</summary>
    [JsonPropertyName("duration")] public double Duration { get; init; }
}

public sealed record CountdownToTimeConfig
{
    /// <summary>Target time of day in seconds. Interpreted against <see cref="Period"/> when that is set.</summary>
    [JsonPropertyName("time_of_day")] public double TimeOfDay { get; init; }

    /// <summary><c>am</c>, <c>pm</c>, or absent for 24-hour interpretation.</summary>
    [JsonPropertyName("period")] public string? Period { get; init; }
}

public sealed record ElapsedConfig
{
    [JsonPropertyName("start_time")] public double StartTime { get; init; }

    /// <summary>Optional target. Zero/absent means the timer counts up without a defined end.</summary>
    [JsonPropertyName("end_time")] public double? EndTime { get; init; }
}

/// <summary>A timer definition from <c>/v1/timers</c>. Exactly one of the three config blocks is populated.</summary>
public sealed record TimerConfiguration
{
    [JsonPropertyName("id")] public required ProId Id { get; init; }
    [JsonPropertyName("allows_overrun")] public bool AllowsOverrun { get; init; }
    [JsonPropertyName("countdown")] public CountdownConfig? Countdown { get; init; }
    [JsonPropertyName("count_down_to_time")] public CountdownToTimeConfig? CountDownToTime { get; init; }
    [JsonPropertyName("elapsed")] public ElapsedConfig? Elapsed { get; init; }

    [JsonIgnore]
    public TimerKind Kind =>
        Countdown is not null ? TimerKind.Countdown :
        CountDownToTime is not null ? TimerKind.CountDownToTime :
        Elapsed is not null ? TimerKind.Elapsed :
        TimerKind.Unknown;

    /// <summary>
    /// Total run length, where it can be known ahead of time. Null for count-down-to-time (depends on
    /// the wall clock at the moment it starts) and for elapsed timers with no end time.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? TotalDuration => Kind switch
    {
        TimerKind.Countdown => TimeSpan.FromSeconds(Countdown!.Duration),
        TimerKind.Elapsed when Elapsed!.EndTime is > 0 =>
            TimeSpan.FromSeconds(Elapsed.EndTime!.Value - Elapsed.StartTime),
        _ => null,
    };
}

public enum TimerKind
{
    Unknown,
    Countdown,
    CountDownToTime,
    Elapsed,
}

/// <summary>Timer operations accepted by <c>/v1/timer/{id}/{operation}</c>.</summary>
public enum TimerOperation
{
    Start,
    Stop,
    Reset,
}

public sealed record MacroInfo
{
    [JsonPropertyName("id")] public required ProId Id { get; init; }
}

public sealed record PlaylistInfo
{
    [JsonPropertyName("id")] public required ProId Id { get; init; }
    [JsonPropertyName("field_type")] public string? FieldType { get; init; }
    [JsonPropertyName("children")] public List<PlaylistInfo>? Children { get; init; }
}

public sealed record NamedItem
{
    [JsonPropertyName("id")] public required ProId Id { get; init; }
}

public sealed record VersionInfo
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("platform")] public string? Platform { get; init; }
    [JsonPropertyName("os_version")] public string? OsVersion { get; init; }
    [JsonPropertyName("host_description")] public string? HostDescription { get; init; }
    [JsonPropertyName("api_version")] public string? ApiVersion { get; init; }
}

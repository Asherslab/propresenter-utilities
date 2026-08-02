using System.Text.Json.Serialization;
using ProPresenter.Api;
using ProPresenter.Api.Actions;

namespace ProPresenter.Triggers.Model;

/// <summary>What a rule watches.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TriggerSource>))]
public enum TriggerSource
{
    /// <summary>A ProPresenter timer reaching a point, described by <see cref="TriggerKind"/>.</summary>
    Timer,

    /// <summary>
    /// An external signal — a RossTalk command sent by a ProPresenter macro, or an HTTP webhook.
    /// This is how a macro reaches the same set of actions a timer can.
    /// </summary>
    Signal,
}

/// <summary>What causes a rule to fire.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TriggerKind>))]
public enum TriggerKind
{
    /// <summary>The timer transitions into the running state.</summary>
    OnStart,

    /// <summary>The timer transitions from running back to stopped without completing.</summary>
    OnStop,

    /// <summary>
    /// The timer reaches its end: the transition into <c>complete</c> or <c>overrunning</c>, whichever
    /// happens first. Behaves identically whether or not the timer allows overrun.
    /// </summary>
    OnEnd,

    /// <summary>The timer finishes its overrun period (transition into <c>overran</c>).</summary>
    OnOverran,

    /// <summary>The timer enters a specific state, for cases the shorthand kinds do not cover.</summary>
    OnStateChange,

    /// <summary>The timer's displayed clock crosses a given value, in either direction.</summary>
    AtTimerValue,

    /// <summary>A given duration after the timer started.</summary>
    AtElapsed,

    /// <summary>A given duration before the timer's end.</summary>
    AtRemaining,
}

/// <summary>
/// Identifies a ProPresenter timer. UUID is authoritative; the name is kept so the UI can still show
/// something meaningful when a timer has been deleted, and so rules survive a UUID change on re-import.
/// </summary>
public sealed record TimerRef
{
    public string? Uuid { get; set; }
    public string? Name { get; set; }

    public bool Matches(ProId id) =>
        (!string.IsNullOrWhiteSpace(Uuid) && string.Equals(Uuid, id.Uuid, StringComparison.OrdinalIgnoreCase))
        || (string.IsNullOrWhiteSpace(Uuid)
            && !string.IsNullOrWhiteSpace(Name)
            && string.Equals(Name, id.Name, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore] public string Display => Name ?? Uuid ?? "(unassigned)";

    public static TimerRef From(ProId id) => new() { Uuid = id.Uuid, Name = id.Name };
}

/// <summary>One trigger: a condition on a timer plus the actions to run when it is met.</summary>
public sealed record TriggerRule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Optional label. When blank the UI renders a description derived from the condition.</summary>
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Whether this rule watches a timer or an external signal.</summary>
    public TriggerSource Source { get; set; } = TriggerSource.Timer;

    public TimerRef Timer { get; set; } = new();

    /// <summary>
    /// The signal this rule listens for when <see cref="Source"/> is <see cref="TriggerSource.Signal"/>,
    /// for example <c>GPI 1</c>, <c>CC 3</c> or <c>webhook:start-service</c>. Matched case-insensitively.
    /// </summary>
    public string SignalKey { get; set; } = string.Empty;

    public TriggerKind Kind { get; set; } = TriggerKind.OnEnd;

    /// <summary>
    /// The threshold for <see cref="TriggerKind.AtTimerValue"/>, <see cref="TriggerKind.AtElapsed"/>
    /// and <see cref="TriggerKind.AtRemaining"/>. Ignored by the state-based kinds.
    /// </summary>
    public TimeSpan Offset { get; set; }

    /// <summary>The state to watch for when <see cref="Kind"/> is <see cref="TriggerKind.OnStateChange"/>.</summary>
    public TimerState TargetState { get; set; } = TimerState.Complete;

    public List<ActionInvocation> Actions { get; set; } = [];

    /// <summary>Human-readable description of the condition, used throughout the UI.</summary>
    [JsonIgnore]
    public string ConditionDescription => Source == TriggerSource.Signal
        ? SignalDescription
        : TimerConditionDescription;

    [JsonIgnore]
    private string SignalDescription
    {
        get
        {
            var signal = string.IsNullOrWhiteSpace(SignalKey) ? "(no signal set)" : SignalKey;
            return Offset > TimeSpan.Zero
                ? $"{TimeCode.Format(Offset)} after signal {signal}"
                : $"on signal {signal}";
        }
    }

    [JsonIgnore]
    private string TimerConditionDescription => Kind switch
    {
        TriggerKind.OnStart => "when the timer starts",
        TriggerKind.OnStop => "when the timer is stopped",
        TriggerKind.OnEnd => "when the timer ends",
        TriggerKind.OnOverran => "when the timer finishes overrunning",
        TriggerKind.OnStateChange => $"when the timer becomes {TargetState.ToString().ToLowerInvariant()}",
        TriggerKind.AtTimerValue => $"when the clock reaches {TimeCode.Format(Offset)}",
        TriggerKind.AtElapsed => $"{TimeCode.Format(Offset)} after the start",
        TriggerKind.AtRemaining => $"{TimeCode.Format(Offset)} before the end",
        _ => Kind.ToString(),
    };

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? char.ToUpperInvariant(ConditionDescription[0]) + ConditionDescription[1..]
        : Name;

    /// <summary>True when the kind uses <see cref="Offset"/>.</summary>
    [JsonIgnore]
    public bool UsesOffset => Source == TriggerSource.Timer
        && Kind is TriggerKind.AtTimerValue or TriggerKind.AtElapsed or TriggerKind.AtRemaining;

    /// <summary>What this rule watches, for display in lists: a timer name or a signal key.</summary>
    [JsonIgnore]
    public string SourceDisplay => Source == TriggerSource.Signal
        ? (string.IsNullOrWhiteSpace(SignalKey) ? "(no signal)" : SignalKey)
        : Timer.Display;

    /// <summary>True when this rule listens for <paramref name="signalKey"/>.</summary>
    public bool MatchesSignal(string signalKey) =>
        Source == TriggerSource.Signal
        && !string.IsNullOrWhiteSpace(SignalKey)
        && string.Equals(SignalKey.Trim(), signalKey.Trim(), StringComparison.OrdinalIgnoreCase);
}

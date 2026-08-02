namespace ProPresenter.Api.Actions;

/// <summary>
/// What kind of value a parameter holds. Drives which picker the UI shows and, where a lookup
/// exists, which ProPresenter list is used to populate it.
/// </summary>
public enum ParameterKind
{
    /// <summary>Free text.</summary>
    Text,

    /// <summary>Whole number, typically a zero-based index.</summary>
    Integer,

    /// <summary>Signed whole number of seconds.</summary>
    Seconds,

    /// <summary>Boolean, rendered as a switch.</summary>
    Boolean,

    /// <summary>One of a fixed set of values supplied by <see cref="ActionParameter.Choices"/>.</summary>
    Choice,

    /// <summary>Raw JSON body, for the few actions whose payload has no simpler shape.</summary>
    Json,

    // Lookup-backed identifiers. Each maps to a ProPresenter list endpoint.
    MacroId,
    TimerId,
    PresentationPlaylistId,
    AudioPlaylistId,
    MediaPlaylistId,
    LookId,
    PropId,
    ClearGroupId,
    MessageId,
    StageLayoutId,
    StageScreenId,
    VideoInputId,
    GroupId,
    LibraryId,
}

public sealed record ActionParameter(
    string Key,
    string Label,
    ParameterKind Kind,
    string? Help = null,
    string? Default = null,
    IReadOnlyList<string>? Choices = null)
{
    public bool IsLookup => Kind is not (ParameterKind.Text or ParameterKind.Integer or ParameterKind.Seconds
        or ParameterKind.Boolean or ParameterKind.Choice or ParameterKind.Json);
}

/// <summary>Where the request payload comes from, for the handful of actions that need one.</summary>
public enum BodySource
{
    None,

    /// <summary>Send the named parameter's value as a raw JSON document.</summary>
    JsonParameter,

    /// <summary>Send the named parameter's value as a JSON string literal.</summary>
    StringParameter,

    /// <summary>Send the named parameter's value as a JSON boolean.</summary>
    BooleanParameter,

    /// <summary>Send the named parameter's value as a JSON number.</summary>
    NumberParameter,
}

/// <summary>
/// One thing this utility can tell ProPresenter to do when a trigger fires.
/// </summary>
/// <param name="Id">Stable identifier persisted in profile files. Never change these.</param>
/// <param name="PathTemplate">API path with <c>{key}</c> placeholders matching parameter keys.</param>
public sealed record ActionDefinition(
    string Id,
    string Category,
    string Name,
    string Description,
    string Method,
    string PathTemplate,
    IReadOnlyList<ActionParameter> Parameters,
    BodySource BodySource = BodySource.None,
    string? BodyParameterKey = null)
{
    public HttpMethod HttpMethod => Method switch
    {
        "POST" => System.Net.Http.HttpMethod.Post,
        "PUT" => System.Net.Http.HttpMethod.Put,
        "DELETE" => System.Net.Http.HttpMethod.Delete,
        _ => System.Net.Http.HttpMethod.Get,
    };
}

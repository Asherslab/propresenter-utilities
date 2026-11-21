using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ProPresenter.Api.Actions;

/// <summary>
/// An action reference plus the argument values chosen for it. Mutable so the editor can bind to it
/// directly; it is serialised into profile files as-is.
/// </summary>
public sealed record ActionInvocation
{
    public string ActionId { get; set; } = string.Empty;

    public Dictionary<string, string> Arguments { get; set; } = [];

    /// <summary>Creates an invocation pre-filled with the action's default argument values.</summary>
    public static ActionInvocation ForAction(ActionDefinition definition)
    {
        var invocation = new ActionInvocation { ActionId = definition.Id };
        foreach (var parameter in definition.Parameters)
        {
            if (parameter.Default is { } value) invocation.Arguments[parameter.Key] = value;
        }

        return invocation;
    }
}

public sealed record ActionOutcome(bool Success, string Summary, string? Error = null);

/// <summary>
/// Turns an <see cref="ActionInvocation"/> into an API call, or hands it to the
/// <see cref="IActionHandler"/> for its target.
/// </summary>
public sealed class ActionExecutor(
    ProPresenterClient client,
    IEnumerable<IActionHandler> handlers,
    ILogger<ActionExecutor> logger)
{
    private readonly Dictionary<ActionTarget, IActionHandler> _handlers = handlers.ToDictionary(h => h.Target);

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct = default)
    {
        var definition = ActionCatalog.Find(invocation.ActionId);
        if (definition is null)
        {
            return new ActionOutcome(false, invocation.ActionId, $"Unknown action '{invocation.ActionId}'.");
        }

        if (definition.Target != ActionTarget.ProPresenter)
        {
            return _handlers.TryGetValue(definition.Target, out var handler)
                ? await handler.ExecuteAsync(definition, invocation, ct)
                : new ActionOutcome(false, Describe(definition, invocation), $"No handler for {definition.Target} actions.");
        }

        string path;
        object? body;
        try
        {
            path = BuildPath(definition, invocation.Arguments);
            body = BuildBody(definition, invocation.Arguments);
        }
        catch (Exception ex)
        {
            return new ActionOutcome(false, Describe(definition, invocation), ex.Message);
        }

        var result = await client.SendAsync(definition.HttpMethod, path, body, ct);
        var summary = Describe(definition, invocation);

        if (!result.Success)
        {
            logger.LogWarning("Action {Action} failed: {Error}", definition.Id, result.Error);
            return new ActionOutcome(false, summary, result.Error);
        }

        return new ActionOutcome(true, summary);
    }

    /// <summary>Human-readable one-liner for logs and the activity feed.</summary>
    public static string Describe(ActionDefinition definition, ActionInvocation invocation)
    {
        var args = definition.Parameters
            .Where(p => p.Kind != ParameterKind.Json)
            .Select(p => invocation.Arguments.GetValueOrDefault(p.Key))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        return args.Count == 0 ? definition.Name : $"{definition.Name} ({string.Join(", ", args)})";
    }

    public static string Describe(ActionInvocation invocation)
    {
        var definition = ActionCatalog.Find(invocation.ActionId);
        return definition is null ? invocation.ActionId : Describe(definition, invocation);
    }

    private static string BuildPath(ActionDefinition definition, Dictionary<string, string> arguments)
    {
        var path = definition.PathTemplate;

        foreach (var parameter in definition.Parameters)
        {
            var placeholder = $"{{{parameter.Key}}}";
            if (!path.Contains(placeholder, StringComparison.Ordinal)) continue;

            var value = arguments.GetValueOrDefault(parameter.Key) ?? parameter.Default;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"'{parameter.Label}' is required for {definition.Name}.");
            }

            path = path.Replace(placeholder, Uri.EscapeDataString(value.Trim()), StringComparison.Ordinal);
        }

        if (path.Contains('{'))
        {
            throw new InvalidOperationException($"{definition.Name} has unfilled parameters.");
        }

        return path;
    }

    private static object? BuildBody(ActionDefinition definition, Dictionary<string, string> arguments)
    {
        if (definition.BodySource == BodySource.None || definition.BodyParameterKey is null) return null;

        var parameter = definition.Parameters.FirstOrDefault(p => p.Key == definition.BodyParameterKey);
        var raw = arguments.GetValueOrDefault(definition.BodyParameterKey) ?? parameter?.Default ?? string.Empty;

        return definition.BodySource switch
        {
            BodySource.StringParameter => JsonValue.Create(raw),
            BodySource.BooleanParameter => JsonValue.Create(
                bool.TryParse(raw, out var flag) && flag),
            BodySource.NumberParameter => JsonValue.Create(
                double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : throw new InvalidOperationException($"'{parameter?.Label ?? "value"}' must be a number.")),
            BodySource.JsonParameter => ParseJson(raw, parameter?.Label ?? definition.BodyParameterKey),
            _ => null,
        };
    }

    private static JsonNode? ParseJson(string raw, string label)
    {
        if (string.IsNullOrWhiteSpace(raw)) return JsonNode.Parse("[]");

        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"'{label}' is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>Parses an integer-valued argument, tolerating whitespace.</summary>
    public static bool TryParseInt(string? value, out int result) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}

using System.Text.Json;
using System.Text.RegularExpressions;
using ProPresenter.Api.Actions;

namespace ProPresenter.Triggers.Tests;

/// <summary>
/// Validates the action catalog against ProPresenter's published OpenAPI document.
/// </summary>
/// <remarks>
/// Catalog entries are hand-written, and a mistyped path or a placeholder with no matching parameter
/// would only surface as a 404 at the moment a cue fires during a live service. This checks the whole
/// catalog at build time instead.
/// </remarks>
public class ActionCatalogTests
{
    private static readonly Lazy<Dictionary<string, HashSet<string>>> Spec = new(LoadSpec);

    /// <summary>
    /// The spec ships as a JavaScript assignment (<c>var openapi_spec = {...}</c>) rather than bare
    /// JSON, so the prefix is trimmed before parsing.
    /// </summary>
    private static Dictionary<string, HashSet<string>> LoadSpec()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "swagger.json");
        var text = File.ReadAllText(path);
        var json = text[text.IndexOf('{')..].TrimEnd().TrimEnd(';');

        using var document = JsonDocument.Parse(json);
        var paths = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var path_ in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            var methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var operation in path_.Value.EnumerateObject())
            {
                if (operation.NameEquals("parameters")) continue;
                methods.Add(operation.Name);
            }

            paths[path_.Name.TrimStart('/')] = methods;
        }

        return paths;
    }

    public static TheoryData<string> AllActionIds()
    {
        var data = new TheoryData<string>();
        foreach (var action in ActionCatalog.All) data.Add(action.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllActionIds))]
    public void Every_action_targets_a_real_endpoint(string actionId)
    {
        var action = ActionCatalog.Find(actionId)!;

        Assert.True(
            Spec.Value.TryGetValue(action.PathTemplate, out var methods),
            $"'{action.Id}' targets '{action.PathTemplate}', which is not in the ProPresenter API.");

        Assert.True(
            methods!.Contains(action.Method),
            $"'{action.Id}' uses {action.Method} on '{action.PathTemplate}', which only supports "
            + $"{string.Join(", ", methods.Select(m => m.ToUpperInvariant()))}.");
    }

    [Theory]
    [MemberData(nameof(AllActionIds))]
    public void Every_path_placeholder_has_a_matching_parameter(string actionId)
    {
        var action = ActionCatalog.Find(actionId)!;
        var placeholders = Regex.Matches(action.PathTemplate, @"\{(\w+)\}").Select(m => m.Groups[1].Value);

        foreach (var placeholder in placeholders)
        {
            Assert.True(
                action.Parameters.Any(p => p.Key == placeholder),
                $"'{action.Id}' has no parameter for the '{{{placeholder}}}' placeholder.");
        }
    }

    [Theory]
    [MemberData(nameof(AllActionIds))]
    public void Body_carrying_actions_name_a_parameter_that_exists(string actionId)
    {
        var action = ActionCatalog.Find(actionId)!;
        if (action.BodySource == BodySource.None)
        {
            Assert.Null(action.BodyParameterKey);
            return;
        }

        Assert.NotNull(action.BodyParameterKey);
        Assert.Contains(action.Parameters, p => p.Key == action.BodyParameterKey);
    }

    [Fact]
    public void Action_ids_are_unique()
    {
        var duplicates = ActionCatalog.All
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Choice_parameters_offer_choices_and_a_valid_default()
    {
        foreach (var action in ActionCatalog.All)
        {
            foreach (var parameter in action.Parameters.Where(p => p.Kind == ParameterKind.Choice))
            {
                Assert.NotNull(parameter.Choices);
                Assert.NotEmpty(parameter.Choices!);
                Assert.Contains(parameter.Default, parameter.Choices!);
            }
        }
    }

    [Fact]
    public void Every_action_has_a_category_name_and_description()
    {
        foreach (var action in ActionCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Category), $"{action.Id} has no category");
            Assert.False(string.IsNullOrWhiteSpace(action.Name), $"{action.Id} has no name");
            Assert.False(string.IsNullOrWhiteSpace(action.Description), $"{action.Id} has no description");
        }
    }
}

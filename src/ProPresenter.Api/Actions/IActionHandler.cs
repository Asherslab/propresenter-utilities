namespace ProPresenter.Api.Actions;

/// <summary>
/// Executes catalog actions that are not ProPresenter API calls — those whose
/// <see cref="ActionDefinition.Target"/> is something other than <see cref="ActionTarget.ProPresenter"/>.
/// </summary>
public interface IActionHandler
{
    ActionTarget Target { get; }

    Task<ActionOutcome> ExecuteAsync(ActionDefinition definition, ActionInvocation invocation, CancellationToken ct);
}

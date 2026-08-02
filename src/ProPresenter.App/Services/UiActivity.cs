namespace ProPresenter.App.Services;

/// <summary>
/// Tracks whether an operator is currently working in the UI.
/// </summary>
/// <remarks>
/// ProPresenter's lists (macros, playlists, looks) are polled rather than pushed, so there is a
/// trade-off between a responsive editor and hammering a machine that is running a service. This
/// splits the difference: while someone is actively editing, lists refresh quickly; once they stop,
/// polling backs off to a long interval.
/// </remarks>
public sealed class UiActivity
{
    /// <summary>How long after the last interaction the operator is still considered active.</summary>
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(90);

    private long _lastTouchTicks = DateTimeOffset.MinValue.UtcTicks;

    /// <summary>Records an interaction. Called on navigation and on edits.</summary>
    public void Touch() => Interlocked.Exchange(ref _lastTouchTicks, DateTimeOffset.UtcNow.UtcTicks);

    public DateTimeOffset LastTouch => new(Interlocked.Read(ref _lastTouchTicks), TimeSpan.Zero);

    public bool IsActive => DateTimeOffset.UtcNow - LastTouch < ActiveWindow;
}

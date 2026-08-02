using System.Collections.Concurrent;

namespace ProPresenter.Triggers;

public enum ActivityLevel
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record ActivityEntry(DateTimeOffset At, ActivityLevel Level, string Message, string? Detail = null);

/// <summary>
/// A bounded in-memory ring of recent events, shown in the UI so an operator can confirm at a glance
/// that cues actually fired during a service.
/// </summary>
public sealed class ActivityLog
{
    private const int Capacity = 300;

    private readonly ConcurrentQueue<ActivityEntry> _entries = new();

    public event Action? Changed;

    public IReadOnlyList<ActivityEntry> Recent => _entries.Reverse().ToList();

    public void Add(ActivityLevel level, string message, string? detail = null)
    {
        _entries.Enqueue(new ActivityEntry(DateTimeOffset.Now, level, message, detail));
        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }

        Changed?.Invoke();
    }

    public void Info(string message, string? detail = null) => Add(ActivityLevel.Info, message, detail);
    public void Success(string message, string? detail = null) => Add(ActivityLevel.Success, message, detail);
    public void Warning(string message, string? detail = null) => Add(ActivityLevel.Warning, message, detail);
    public void Error(string message, string? detail = null) => Add(ActivityLevel.Error, message, detail);

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        Changed?.Invoke();
    }
}

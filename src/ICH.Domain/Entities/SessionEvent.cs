namespace ICH.Domain.Entities;

/// <summary>
/// Tracks events within a session for audit and analytics.
/// </summary>
public class SessionEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public Session Session { get; set; } = null!;
}

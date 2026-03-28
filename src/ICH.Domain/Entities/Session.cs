namespace ICH.Domain.Entities;

/// <summary>
/// Represents an audio communication session.
/// </summary>
public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAt { get; set; }
    public string SourceLanguage { get; set; } = "en";
    public string TargetLanguage { get; set; } = "es";
    public SessionStatus Status { get; set; } = SessionStatus.Created;
    public string? Summary { get; set; }
    public string? RecordingBlobUrl { get; set; }
    public bool ConsentGiven { get; set; }
    public bool IsRecordingEnabled { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<TranscriptEntry> TranscriptEntries { get; set; } = new List<TranscriptEntry>();
    public ICollection<SessionEvent> Events { get; set; } = new List<SessionEvent>();

    public void Start()
    {
        Status = SessionStatus.Active;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void Pause() => Status = SessionStatus.Paused;
    public void Resume() => Status = SessionStatus.Active;

    public void Complete()
    {
        Status = SessionStatus.Completed;
        EndedAt = DateTimeOffset.UtcNow;
    }

    public void Archive() => Status = SessionStatus.Archived;
}

public enum SessionStatus
{
    Created,
    Active,
    Paused,
    Completed,
    Archived
}

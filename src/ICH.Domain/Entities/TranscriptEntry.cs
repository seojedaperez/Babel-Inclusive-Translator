namespace ICH.Domain.Entities;

/// <summary>
/// A single transcript entry from speech-to-text processing.
/// </summary>
public class TranscriptEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? SpeakerId { get; set; }
    public string? SpeakerName { get; set; }
    public double Confidence { get; set; }
    public string? Emotion { get; set; }
    public string PipelineType { get; set; } = "input"; // "input" or "output"
    public TimeSpan Offset { get; set; }
    public TimeSpan Duration { get; set; }

    // Navigation
    public Session Session { get; set; } = null!;
}

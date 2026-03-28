namespace ICH.Shared.DTOs;

/// <summary>
/// Represents a real-time audio processing event.
/// </summary>
public record AudioProcessingEvent
{
    public string SessionId { get; init; } = string.Empty;
    public string PipelineType { get; init; } = string.Empty; // "input" or "output"
    public string OriginalText { get; init; } = string.Empty;
    public string TranslatedText { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = string.Empty;
    public string TargetLanguage { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public double Confidence { get; init; }
    public string? SpeakerId { get; init; }
    public string? Emotion { get; init; }
}

public record SessionDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public string SourceLanguage { get; init; } = string.Empty;
    public string TargetLanguage { get; init; } = string.Empty;
    public SessionStatus Status { get; init; }
    public List<TranscriptEntryDto> Transcripts { get; init; } = [];
    public string? Summary { get; init; }
    public string? RecordingUrl { get; init; }
}

public record TranscriptEntryDto
{
    public Guid Id { get; init; }
    public string OriginalText { get; init; } = string.Empty;
    public string TranslatedText { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = string.Empty;
    public string TargetLanguage { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string? SpeakerId { get; init; }
    public string? SpeakerName { get; init; }
    public double Confidence { get; init; }
    public string? Emotion { get; init; }
}

public record SubtitleDto
{
    public string Text { get; init; } = string.Empty;
    public string TranslatedText { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = string.Empty;
    public string TargetLanguage { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public bool IsFinal { get; init; }
}

public record SignLanguageGesture
{
    public string Word { get; init; } = string.Empty;
    public string AnimationId { get; init; } = string.Empty;
    public int DurationMs { get; init; }
    public int SequenceIndex { get; init; }
}

public record CopilotMessage
{
    public string Role { get; init; } = string.Empty; // "user" or "assistant"
    public string Content { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public record CopilotRequest
{
    public Guid SessionId { get; init; }
    public string Query { get; init; } = string.Empty;
    public List<CopilotMessage> History { get; init; } = [];
}

public record CopilotResponse
{
    public string Answer { get; init; } = string.Empty;
    public List<string> ActionItems { get; init; } = [];
    public string? Summary { get; init; }
}

public record AudioDeviceInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public AudioDeviceType DeviceType { get; init; }
    public bool IsDefault { get; init; }
    public bool IsVirtual { get; init; }
}

public record PipelineStatus
{
    public string SessionId { get; init; } = string.Empty;
    public bool IsInputPipelineActive { get; init; }
    public bool IsOutputPipelineActive { get; init; }
    public string SourceLanguage { get; init; } = string.Empty;
    public string TargetLanguage { get; init; } = string.Empty;
    public double InputLatencyMs { get; init; }
    public double OutputLatencyMs { get; init; }
    public int TotalTranscriptEntries { get; init; }
}

public record UserDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string PreferredLanguage { get; init; } = "en";
    public bool ConsentGiven { get; init; }
}

public record LoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public record LoginResponse
{
    public string Token { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public UserDto User { get; init; } = new();
    public DateTimeOffset ExpiresAt { get; init; }
}

public enum SessionStatus
{
    Created,
    Active,
    Paused,
    Completed,
    Archived
}

public enum AudioDeviceType
{
    Microphone,
    Speaker,
    VirtualMicrophone,
    VirtualSpeaker
}

public enum PipelineType
{
    Input,
    Output
}

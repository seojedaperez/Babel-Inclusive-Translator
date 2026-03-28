namespace ICH.Shared.Hubs;

/// <summary>
/// SignalR hub interface for real-time audio communication.
/// </summary>
public interface IAudioHubClient
{
    /// <summary>Receives live subtitle updates.</summary>
    Task ReceiveSubtitle(string sessionId, string originalText, string translatedText,
        string sourceLanguage, string targetLanguage, bool isFinal);

    /// <summary>Receives a new transcript entry.</summary>
    Task ReceiveTranscript(string sessionId, string originalText, string translatedText,
        string sourceLanguage, string targetLanguage, string? speakerId, double confidence, string? emotion);

    /// <summary>Receives sign language gesture data.</summary>
    Task ReceiveSignLanguageGesture(string sessionId, string word, string animationId,
        int durationMs, int sequenceIndex);

    /// <summary>Receives pipeline status updates.</summary>
    Task ReceivePipelineStatus(string sessionId, bool isInputActive, bool isOutputActive,
        double inputLatencyMs, double outputLatencyMs, int totalEntries);

    /// <summary>Receives session events.</summary>
    Task ReceiveSessionEvent(string sessionId, string eventType, string data);

    /// <summary>Receives emotion detection results.</summary>
    Task ReceiveEmotionDetection(string sessionId, string speakerId, string emotion, double confidence);

    /// <summary>Receives speaker detection results.</summary>
    Task ReceiveSpeakerDetected(string sessionId, string speakerId, string? speakerName);
}

/// <summary>
/// SignalR hub interface for server-side methods.
/// </summary>
public interface IAudioHub
{
    Task JoinSession(string sessionId);
    Task LeaveSession(string sessionId);
    Task StartPipeline(string sessionId, string sourceLanguage, string targetLanguage);
    Task StopPipeline(string sessionId);
    Task SendKeyboardInput(string sessionId, string text, string targetLanguage);
    Task UpdateLanguage(string sessionId, string sourceLanguage, string targetLanguage);
}

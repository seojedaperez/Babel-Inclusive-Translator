using Microsoft.AspNetCore.SignalR;
using ICH.Shared.Hubs;
using ICH.Domain.Entities;
using ICH.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ICH.API.Hubs;

/// <summary>
/// SignalR hub for real-time audio pipeline communication.
/// Connects the background service, MAUI app, and web portal.
/// </summary>
public class AudioHub : Hub<IAudioHubClient>, IAudioHub
{
    private readonly ILogger<AudioHub> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public AudioHub(ILogger<AudioHub> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("Client {ConnectionId} joined session {SessionId}",
            Context.ConnectionId, sessionId);

        await Clients.Group(sessionId).ReceiveSessionEvent(
            sessionId, "client_joined", Context.ConnectionId);
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("Client {ConnectionId} left session {SessionId}",
            Context.ConnectionId, sessionId);
    }

    public async Task StartPipeline(string sessionId, string sourceLanguage, string targetLanguage)
    {
        _logger.LogInformation("Starting pipeline for session {SessionId}: {Source}→{Target}",
            sessionId, sourceLanguage, targetLanguage);

        await Clients.Group(sessionId).ReceiveSessionEvent(
            sessionId, "pipeline_started", $"{sourceLanguage}→{targetLanguage}");
    }

    public async Task StopPipeline(string sessionId)
    {
        _logger.LogInformation("Stopping pipeline for session {SessionId}", sessionId);

        await Clients.Group(sessionId).ReceiveSessionEvent(
            sessionId, "pipeline_stopped", string.Empty);
    }

    public async Task ConfigurePipeline(string sessionId, bool noiseCancellationEnabled)
    {
        _logger.LogInformation("Configuring pipeline for session {SessionId}: NoiseCancellation={Enabled}", 
            sessionId, noiseCancellationEnabled);

        await Clients.Group(sessionId).ReceiveSessionEvent(
            sessionId, "pipeline_configured", $"NoiseCancellation={noiseCancellationEnabled}");
    }

    public async Task SendKeyboardInput(string sessionId, string text, string targetLanguage)
    {
        _logger.LogInformation("Keyboard input for session {SessionId}: {Text}",
            sessionId, text[..Math.Min(30, text.Length)]);

        await Clients.Group(sessionId).ReceiveSessionEvent(
            sessionId, "keyboard_input", text);
    }

    public async Task UpdateLanguage(string sessionId, string sourceLanguage, string targetLanguage)
    {
        _logger.LogInformation("Language updated for session {SessionId}: {Source}→{Target}",
            sessionId, sourceLanguage, targetLanguage);

        await Clients.Group(sessionId).ReceiveSessionEvent(
            sessionId, "language_updated", $"{sourceLanguage}→{targetLanguage}");
    }

    /// <summary>
    /// Send a subtitle to all clients in a session.
    /// Called by the background service.
    /// </summary>
    public async Task BroadcastSubtitle(string sessionId, string originalText, string translatedText,
        string sourceLanguage, string targetLanguage, bool isFinal)
    {
        await Clients.Group(sessionId).ReceiveSubtitle(
            sessionId, originalText, translatedText, sourceLanguage, targetLanguage, isFinal);
    }

    /// <summary>
    /// Send a transcript entry to all clients in a session.
    /// </summary>
    public async Task BroadcastTranscript(string sessionId, string originalText, string translatedText,
        string sourceLanguage, string targetLanguage, string? speakerId, double confidence, string? emotion)
    {
        await Clients.Group(sessionId).ReceiveTranscript(
            sessionId, originalText, translatedText, sourceLanguage, targetLanguage,
            speakerId, confidence, emotion);

        // Store in database
        try
        {
            if (Guid.TryParse(sessionId, out var sessionGuid))
            {
                await _unitOfWork.Transcripts.CreateAsync(new TranscriptEntry
                {
                    SessionId = sessionGuid,
                    OriginalText = originalText,
                    TranslatedText = translatedText,
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    SpeakerId = speakerId,
                    Confidence = confidence,
                    Emotion = emotion,
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store transcript entry");
        }
    }

    /// <summary>
    /// Broadcast sign language gesture data.
    /// </summary>
    public async Task BroadcastSignLanguageGesture(string sessionId, string word, string animationId,
        int durationMs, int sequenceIndex)
    {
        await Clients.Group(sessionId).ReceiveSignLanguageGesture(
            sessionId, word, animationId, durationMs, sequenceIndex);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

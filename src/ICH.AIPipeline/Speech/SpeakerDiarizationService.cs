using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ICH.Shared.Configuration;
using System.Reactive.Subjects;
using System.Reactive.Linq;

namespace ICH.AIPipeline.Speech;

/// <summary>
/// Real-time speaker diarization using Azure Conversation Transcriber.
/// Identifies and labels different speakers in multi-person conversations.
/// Inspired by UTell.ai's Speaker Recognition feature.
/// </summary>
public sealed class SpeakerDiarizationService : IAsyncDisposable
{
    private readonly ILogger<SpeakerDiarizationService> _logger;
    private readonly AzureSpeechSettings _settings;
    private ConversationTranscriber? _transcriber;
    private PushAudioInputStream? _pushStream;
    private readonly Subject<DiarizedTranscript> _transcriptSubject = new();
    private bool _isActive;

    public IObservable<DiarizedTranscript> TranscriptStream => _transcriptSubject.AsObservable();
    public bool IsActive => _isActive;

    public SpeakerDiarizationService(
        ILogger<SpeakerDiarizationService> logger,
        IOptions<AzureSpeechSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    /// <summary>
    /// Start conversation transcription with speaker diarization.
    /// </summary>
    public async Task StartAsync(string language = "en-US", CancellationToken ct = default)
    {
        if (_isActive)
        {
            _logger.LogWarning("Speaker diarization is already active");
            return;
        }

        try
        {
            var speechConfig = SpeechConfig.FromSubscription(_settings.SubscriptionKey, _settings.Region);
            speechConfig.SpeechRecognitionLanguage = language;
            speechConfig.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");

            // Create push stream for feeding audio
            var audioFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            _pushStream = AudioInputStream.CreatePushStream(audioFormat);
            var audioConfig = AudioConfig.FromStreamInput(_pushStream);

            _transcriber = new ConversationTranscriber(speechConfig, audioConfig);

            // Wire up events
            _transcriber.Transcribing += OnTranscribing;
            _transcriber.Transcribed += OnTranscribed;
            _transcriber.Canceled += OnCanceled;
            _transcriber.SessionStarted += (_, e) =>
                _logger.LogInformation("Diarization session started: {SessionId}", e.SessionId);
            _transcriber.SessionStopped += (_, e) =>
                _logger.LogInformation("Diarization session stopped: {SessionId}", e.SessionId);

            await _transcriber.StartTranscribingAsync();
            _isActive = true;

            _logger.LogInformation("Speaker diarization started for language: {Language}", language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start speaker diarization");
            throw;
        }
    }

    /// <summary>
    /// Feed raw audio data to the conversation transcriber.
    /// </summary>
    public void PushAudioData(byte[] audioData)
    {
        if (!_isActive || _pushStream == null) return;

        try
        {
            _pushStream.Write(audioData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing audio data to diarization");
        }
    }

    /// <summary>
    /// Stop conversation transcription.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isActive || _transcriber == null) return;

        try
        {
            _pushStream?.Close();
            await _transcriber.StopTranscribingAsync();
            _isActive = false;
            _logger.LogInformation("Speaker diarization stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping speaker diarization");
        }
    }

    private void OnTranscribing(object? sender, ConversationTranscriptionEventArgs e)
    {
        if (e.Result.Reason == ResultReason.RecognizingSpeech && !string.IsNullOrEmpty(e.Result.Text))
        {
            _transcriptSubject.OnNext(new DiarizedTranscript
            {
                Text = e.Result.Text,
                SpeakerId = e.Result.SpeakerId ?? "Unknown",
                IsFinal = false,
                Offset = e.Result.OffsetInTicks,
                Duration = e.Result.Duration,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
    }

    private void OnTranscribed(object? sender, ConversationTranscriptionEventArgs e)
    {
        if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
        {
            _transcriptSubject.OnNext(new DiarizedTranscript
            {
                Text = e.Result.Text,
                SpeakerId = e.Result.SpeakerId ?? "Unknown",
                IsFinal = true,
                Offset = e.Result.OffsetInTicks,
                Duration = e.Result.Duration,
                Timestamp = DateTimeOffset.UtcNow
            });

            _logger.LogDebug("[{Speaker}] {Text}", e.Result.SpeakerId, e.Result.Text);
        }
    }

    private void OnCanceled(object? sender, ConversationTranscriptionCanceledEventArgs e)
    {
        if (e.Reason == CancellationReason.Error)
        {
            _logger.LogError("Diarization canceled: {ErrorCode} - {ErrorDetails}",
                e.ErrorCode, e.ErrorDetails);
        }
        _isActive = false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _transcriber?.Dispose();
        _pushStream?.Dispose();
        _transcriptSubject.Dispose();
    }
}

/// <summary>
/// Transcript entry with speaker identification.
/// </summary>
public record DiarizedTranscript
{
    public string Text { get; init; } = string.Empty;
    public string SpeakerId { get; init; } = "Unknown";
    public bool IsFinal { get; init; }
    public long Offset { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

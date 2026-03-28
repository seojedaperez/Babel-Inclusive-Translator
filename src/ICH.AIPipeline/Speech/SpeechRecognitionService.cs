using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ICH.Shared.Configuration;
using System.Reactive.Subjects;
using System.Reactive.Linq;

namespace ICH.AIPipeline.Speech;

/// <summary>
/// Real-time speech-to-text using Azure Cognitive Services.
/// Processes audio streams and emits recognized text.
/// </summary>
public sealed class SpeechRecognitionService : IAsyncDisposable
{
    private readonly ILogger<SpeechRecognitionService> _logger;
    private readonly AzureSpeechSettings _settings;
    private SpeechRecognizer? _recognizer;
    private PushAudioInputStream? _pushStream;
    private readonly Subject<RecognitionResult> _recognitionSubject = new();
    private bool _isRecognizing;

    public IObservable<RecognitionResult> RecognitionStream => _recognitionSubject.AsObservable();
    public bool IsRecognizing => _isRecognizing;

    public SpeechRecognitionService(
        ILogger<SpeechRecognitionService> logger,
        IOptions<AzureSpeechSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    /// <summary>
    /// Start continuous speech recognition.
    /// </summary>
    public async Task StartRecognitionAsync(string language = "en-US", CancellationToken ct = default)
    {
        if (_isRecognizing)
        {
            _logger.LogWarning("Speech recognition is already active");
            return;
        }

        try
        {
            var speechConfig = SpeechConfig.FromSubscription(_settings.SubscriptionKey, _settings.Region);
            speechConfig.SpeechRecognitionLanguage = language;
            speechConfig.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
            speechConfig.EnableDictation();

            // Enable speaker diarization
            speechConfig.SetProperty("DiarizationEnabled", "true");

            // Create push stream for feeding audio
            var audioFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            _pushStream = AudioInputStream.CreatePushStream(audioFormat);
            var audioConfig = AudioConfig.FromStreamInput(_pushStream);

            _recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            // Wire up events
            _recognizer.Recognizing += OnRecognizing;
            _recognizer.Recognized += OnRecognized;
            _recognizer.Canceled += OnCanceled;
            _recognizer.SessionStarted += (_, e) =>
                _logger.LogInformation("Speech recognition session started: {SessionId}", e.SessionId);
            _recognizer.SessionStopped += (_, e) =>
                _logger.LogInformation("Speech recognition session stopped: {SessionId}", e.SessionId);

            await _recognizer.StartContinuousRecognitionAsync();
            _isRecognizing = true;

            _logger.LogInformation("Speech recognition started for language: {Language}", language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start speech recognition");
            throw;
        }
    }

    /// <summary>
    /// Feed raw audio data to the speech recognizer.
    /// </summary>
    public void PushAudioData(byte[] audioData)
    {
        if (!_isRecognizing || _pushStream == null) return;

        try
        {
            _pushStream.Write(audioData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing audio data to recognizer");
        }
    }

    /// <summary>
    /// Stop continuous speech recognition.
    /// </summary>
    public async Task StopRecognitionAsync()
    {
        if (!_isRecognizing || _recognizer == null) return;

        try
        {
            _pushStream?.Close();
            await _recognizer.StopContinuousRecognitionAsync();
            _isRecognizing = false;
            _logger.LogInformation("Speech recognition stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping speech recognition");
        }
    }

    private void OnRecognizing(object? sender, SpeechRecognitionEventArgs e)
    {
        if (e.Result.Reason == ResultReason.RecognizingSpeech && !string.IsNullOrEmpty(e.Result.Text))
        {
            _recognitionSubject.OnNext(new RecognitionResult
            {
                Text = e.Result.Text,
                IsFinal = false,
                Confidence = 0,
                Offset = e.Result.OffsetInTicks,
                Duration = e.Result.Duration,
                Language = e.Result.Properties.GetProperty(PropertyId.SpeechServiceConnection_AutoDetectSourceLanguageResult, "unknown"),
                Timestamp = DateTimeOffset.UtcNow
            });
        }
    }

    private void OnRecognized(object? sender, SpeechRecognitionEventArgs e)
    {
        if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
        {
            var confidence = 0.0;
            try
            {
                var detailedResults = e.Result.Best();
                if (detailedResults != null && detailedResults.Any())
                {
                    confidence = detailedResults.First().Confidence;
                }
            }
            catch { /* Detailed results not always available */ }

            _recognitionSubject.OnNext(new RecognitionResult
            {
                Text = e.Result.Text,
                IsFinal = true,
                Confidence = confidence,
                Offset = e.Result.OffsetInTicks,
                Duration = e.Result.Duration,
                Language = e.Result.Properties.GetProperty(PropertyId.SpeechServiceConnection_AutoDetectSourceLanguageResult, "unknown"),
                SpeakerId = ExtractSpeakerId(e.Result),
                Timestamp = DateTimeOffset.UtcNow
            });

            _logger.LogDebug("Recognized: {Text} (Confidence: {Confidence:P})", e.Result.Text, confidence);
        }
    }

    private void OnCanceled(object? sender, SpeechRecognitionCanceledEventArgs e)
    {
        if (e.Reason == CancellationReason.Error)
        {
            _logger.LogError("Speech recognition canceled: {ErrorCode} - {ErrorDetails}",
                e.ErrorCode, e.ErrorDetails);
        }
        _isRecognizing = false;
    }

    private static string? ExtractSpeakerId(SpeechRecognitionResult result)
    {
        try
        {
            var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
            if (!string.IsNullOrEmpty(json) && json.Contains("SpeakerId"))
            {
                // Simple extraction - in production use proper JSON parsing
                var start = json.IndexOf("\"SpeakerId\":\"") + 13;
                var end = json.IndexOf("\"", start);
                if (start > 12 && end > start)
                    return json[start..end];
            }
        }
        catch { }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopRecognitionAsync();
        _recognizer?.Dispose();
        _pushStream?.Dispose();
        _recognitionSubject.Dispose();
    }
}

/// <summary>
/// Result from speech recognition.
/// </summary>
public record RecognitionResult
{
    public string Text { get; init; } = string.Empty;
    public bool IsFinal { get; init; }
    public double Confidence { get; init; }
    public long Offset { get; init; }
    public TimeSpan Duration { get; init; }
    public string Language { get; init; } = string.Empty;
    public string? SpeakerId { get; init; }
    public string? Emotion { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

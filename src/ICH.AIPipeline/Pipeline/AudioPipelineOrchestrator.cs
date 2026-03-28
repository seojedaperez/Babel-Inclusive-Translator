using Microsoft.Extensions.Logging;
using ICH.AIPipeline.Speech;
using ICH.AIPipeline.Translation;
using ICH.AudioEngine.Capture;
using ICH.AudioEngine.Processing;
using ICH.AudioEngine.VirtualDevices;
using ICH.Shared.DTOs;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Diagnostics;

namespace ICH.AIPipeline.Pipeline;

/// <summary>
/// Orchestrates the real-time audio processing pipeline:
/// Audio Input → Enhancement → Noise Cancellation → STT → Translation → Accent Conversion → TTS → Audio Output
/// 
/// Manages both input (microphone) and output (system audio) pipelines.
/// Enhanced with UTell.ai-inspired features: noise cancellation, audio enhancement, accent conversion.
/// </summary>
public sealed class AudioPipelineOrchestrator : IAsyncDisposable
{
    private readonly ILogger<AudioPipelineOrchestrator> _logger;
    private readonly SpeechRecognitionService _sttService;
    private readonly TranslationService _translationService;
    private readonly SpeechSynthesisService _ttsService;
    private readonly AudioFormatConverter _formatConverter;
    private readonly NoiseCancellationProcessor _noiseCancellation;
    private readonly AudioEnhancementProcessor _audioEnhancement;
    private readonly AccentConversionService _accentConversion;
    private readonly SpeakerDiarizationService _diarization;

    private readonly Subject<AudioProcessingEvent> _processingEvents = new();
    private readonly Subject<SubtitleDto> _subtitles = new();
    private readonly Subject<SignLanguageGesture> _signLanguageGestures = new();

    private IDisposable? _inputPipelineSubscription;
    private IDisposable? _outputPipelineSubscription;

    private string _sourceLanguage = "en-US";
    private string _targetLanguage = "es";
    private string _sessionId = string.Empty;
    private bool _isInputActive;
    private bool _isOutputActive;

    // Pipeline feature flags
    private bool _noiseCancellationEnabled = true;
    private bool _audioEnhancementEnabled = true;
    private bool _accentConversionEnabled = false;
    private bool _diarizationEnabled = false;

    private readonly Stopwatch _latencyTracker = new();
    private double _lastInputLatencyMs;
    private double _lastOutputLatencyMs;
    private int _totalTranscriptEntries;

    // Public observables for consumers
    public IObservable<AudioProcessingEvent> ProcessingEvents => _processingEvents.AsObservable();
    public IObservable<SubtitleDto> Subtitles => _subtitles.AsObservable();
    public IObservable<SignLanguageGesture> SignLanguageGestures => _signLanguageGestures.AsObservable();

    public bool IsInputActive => _isInputActive;
    public bool IsOutputActive => _isOutputActive;

    public AudioPipelineOrchestrator(
        ILogger<AudioPipelineOrchestrator> logger,
        SpeechRecognitionService sttService,
        TranslationService translationService,
        SpeechSynthesisService ttsService,
        AudioFormatConverter formatConverter,
        NoiseCancellationProcessor noiseCancellation,
        AudioEnhancementProcessor audioEnhancement,
        AccentConversionService accentConversion,
        SpeakerDiarizationService diarization)
    {
        _logger = logger;
        _sttService = sttService;
        _translationService = translationService;
        _ttsService = ttsService;
        _formatConverter = formatConverter;
        _noiseCancellation = noiseCancellation;
        _audioEnhancement = audioEnhancement;
        _accentConversion = accentConversion;
        _diarization = diarization;
    }


    /// <summary>
    /// Configure the pipeline languages.
    /// </summary>
    public void Configure(string sessionId, string sourceLanguage, string targetLanguage)
    {
        _sessionId = sessionId;
        _sourceLanguage = sourceLanguage;
        _targetLanguage = targetLanguage;
        _logger.LogInformation("Pipeline configured: Session={Session}, {Source}→{Target}",
            sessionId, sourceLanguage, targetLanguage);
    }

    /// <summary>
    /// Configure which pipeline features are enabled at runtime.
    /// </summary>
    public void ConfigurePipelineFeatures(
        bool noiseCancellation = true,
        bool audioEnhancement = true,
        bool accentConversion = false,
        bool diarization = false)
    {
        _noiseCancellationEnabled = noiseCancellation;
        _audioEnhancementEnabled = audioEnhancement;
        _accentConversionEnabled = accentConversion;
        _diarizationEnabled = diarization;
        _noiseCancellation.Enabled = noiseCancellation;
        _audioEnhancement.Enabled = audioEnhancement;
        _accentConversion.Enabled = accentConversion;
        _logger.LogInformation(
            "Pipeline features configured: NoiseCancellation={NC}, Enhancement={AE}, AccentConversion={AC}, Diarization={D}",
            noiseCancellation, audioEnhancement, accentConversion, diarization);
    }

    /// <summary>
    /// Start the INPUT pipeline:
    /// Microphone → Enhancement → Noise Cancellation → STT → Translate → Accent Conversion → TTS → Virtual Mic
    /// </summary>
    public async Task StartInputPipelineAsync(
        MicrophoneCapture microphone,
        VirtualMicrophoneOutput? virtualMic = null,
        CancellationToken ct = default)
    {
        if (_isInputActive)
        {
            _logger.LogWarning("Input pipeline is already active");
            return;
        }

        _logger.LogInformation(
            "Starting INPUT pipeline: Mic → Enhancement → Noise Cancel → STT → Translate → TTS → Virtual Mic");

        // Start speech recognition (or diarization if enabled)
        if (_diarizationEnabled)
        {
            await _diarization.StartAsync(_sourceLanguage, ct);
        }
        await _sttService.StartRecognitionAsync(_sourceLanguage, ct);

        // Subscribe to audio data from microphone → preprocess → push to STT
        _inputPipelineSubscription = microphone.AudioStream
            .Subscribe(audioEvent =>
            {
                // Convert format for processing
                var convertedData = _formatConverter.ConvertToSpeechFormat(
                    audioEvent.Buffer, audioEvent.WaveFormat);

                if (convertedData.Length > 0)
                {
                    // Audio processing chain: Enhancement → Noise Cancellation
                    var processedData = convertedData;
                    var speechFormat = AudioFormatConverter.SpeechServiceFormat;

                    if (_audioEnhancementEnabled)
                    {
                        processedData = _audioEnhancement.ProcessAudio(processedData, speechFormat);
                    }

                    if (_noiseCancellationEnabled)
                    {
                        processedData = _noiseCancellation.ProcessAudio(processedData, speechFormat);
                    }

                    _sttService.PushAudioData(processedData);

                    // Also push to diarization if enabled
                    if (_diarizationEnabled)
                    {
                        _diarization.PushAudioData(processedData);
                    }
                }
            });

        // Subscribe to recognition results → translate → synthesize
        _sttService.RecognitionStream
            .Subscribe(async result =>
            {
                _latencyTracker.Restart();

                // Emit subtitle (partial or final)
                if (!result.IsFinal)
                {
                    // For partial results, just show subtitles (no translation yet)
                    _subtitles.OnNext(new SubtitleDto
                    {
                        Text = result.Text,
                        TranslatedText = string.Empty,
                        SourceLanguage = _sourceLanguage,
                        TargetLanguage = _targetLanguage,
                        Timestamp = result.Timestamp,
                        IsFinal = false
                    });
                    return;
                }

                try
                {
                    // Translate
                    var translation = await _translationService.TranslateAsync(
                        result.Text, _targetLanguage, _sourceLanguage, ct);

                    // Emit final subtitle
                    _subtitles.OnNext(new SubtitleDto
                    {
                        Text = result.Text,
                        TranslatedText = translation.TranslatedText,
                        SourceLanguage = _sourceLanguage,
                        TargetLanguage = _targetLanguage,
                        Timestamp = result.Timestamp,
                        IsFinal = true
                    });

                    // Emit processing event for transcript
                    _totalTranscriptEntries++;
                    _processingEvents.OnNext(new AudioProcessingEvent
                    {
                        SessionId = _sessionId,
                        PipelineType = "input",
                        OriginalText = result.Text,
                        TranslatedText = translation.TranslatedText,
                        SourceLanguage = translation.SourceLanguage,
                        TargetLanguage = translation.TargetLanguage,
                        Timestamp = result.Timestamp,
                        Confidence = result.Confidence,
                        SpeakerId = result.SpeakerId,
                        Emotion = result.Emotion
                    });

                    // Generate sign language gestures
                    EmitSignLanguageGestures(translation.TranslatedText);

                    // Synthesize translated speech
                    if (virtualMic != null)
                    {
                        var synthesis = await _ttsService.SynthesizeToAudioAsync(
                            translation.TranslatedText, _targetLanguage, ct);

                        if (synthesis.Success && synthesis.AudioData.Length > 0)
                        {
                            virtualMic.WriteAudio(synthesis.AudioData);
                        }
                    }

                    _latencyTracker.Stop();
                    _lastInputLatencyMs = _latencyTracker.ElapsedMilliseconds;
                    _logger.LogDebug("Input pipeline latency: {Latency}ms", _lastInputLatencyMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in input pipeline processing");
                }
            });

        _isInputActive = true;
        _logger.LogInformation("INPUT pipeline started successfully");
    }

    /// <summary>
    /// Start the OUTPUT pipeline (System Audio → STT → Translate → TTS → Virtual Speaker).
    /// </summary>
    public async Task StartOutputPipelineAsync(
        SystemAudioCapture systemCapture,
        VirtualAudioOutput? virtualSpeaker = null,
        CancellationToken ct = default)
    {
        if (_isOutputActive)
        {
            _logger.LogWarning("Output pipeline is already active");
            return;
        }

        _logger.LogInformation("Starting OUTPUT pipeline: System Audio → STT → Translate → TTS → Virtual Speaker");

        // Create a separate STT instance for output
        // In production, you'd use a second SpeechRecognitionService instance
        var outputStt = _sttService; // Simplified - share STT for now

        // Subscribe to system audio → push to STT
        _outputPipelineSubscription = systemCapture.AudioStream
            .Subscribe(audioEvent =>
            {
                var convertedData = _formatConverter.ConvertToSpeechFormat(
                    audioEvent.Buffer, audioEvent.WaveFormat);

                if (convertedData.Length > 0)
                {
                    outputStt.PushAudioData(convertedData);
                }
            });

        _isOutputActive = true;
        _logger.LogInformation("OUTPUT pipeline started successfully");
    }

    /// <summary>
    /// Process keyboard input text through the pipeline.
    /// </summary>
    public async Task ProcessKeyboardInputAsync(
        string text,
        VirtualMicrophoneOutput? virtualMic = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            _logger.LogInformation("Processing keyboard input: {Text}", text[..Math.Min(30, text.Length)]);

            // Translate
            var translation = await _translationService.TranslateAsync(
                text, _targetLanguage, _sourceLanguage, ct);

            // Emit subtitle
            _subtitles.OnNext(new SubtitleDto
            {
                Text = text,
                TranslatedText = translation.TranslatedText,
                SourceLanguage = _sourceLanguage,
                TargetLanguage = _targetLanguage,
                Timestamp = DateTimeOffset.UtcNow,
                IsFinal = true
            });

            // Emit processing event
            _processingEvents.OnNext(new AudioProcessingEvent
            {
                SessionId = _sessionId,
                PipelineType = "keyboard",
                OriginalText = text,
                TranslatedText = translation.TranslatedText,
                SourceLanguage = translation.SourceLanguage,
                TargetLanguage = translation.TargetLanguage,
                Timestamp = DateTimeOffset.UtcNow
            });

            // Synthesize to virtual mic
            if (virtualMic != null)
            {
                var synthesis = await _ttsService.SynthesizeToAudioAsync(
                    translation.TranslatedText, _targetLanguage, ct);

                if (synthesis.Success)
                {
                    virtualMic.WriteAudio(synthesis.AudioData);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing keyboard input");
        }
    }

    /// <summary>
    /// Update the pipeline languages at runtime.
    /// </summary>
    public void UpdateLanguages(string sourceLanguage, string targetLanguage)
    {
        _sourceLanguage = sourceLanguage;
        _targetLanguage = targetLanguage;
        _logger.LogInformation("Pipeline languages updated: {Source}→{Target}", sourceLanguage, targetLanguage);
    }

    /// <summary>
    /// Get current pipeline status.
    /// </summary>
    public PipelineStatus GetStatus() => new()
    {
        SessionId = _sessionId,
        IsInputPipelineActive = _isInputActive,
        IsOutputPipelineActive = _isOutputActive,
        SourceLanguage = _sourceLanguage,
        TargetLanguage = _targetLanguage,
        InputLatencyMs = _lastInputLatencyMs,
        OutputLatencyMs = _lastOutputLatencyMs,
        TotalTranscriptEntries = _totalTranscriptEntries
    };

    /// <summary>
    /// Stop all pipelines.
    /// </summary>
    public async Task StopAsync()
    {
        _inputPipelineSubscription?.Dispose();
        _outputPipelineSubscription?.Dispose();
        await _sttService.StopRecognitionAsync();
        
        if (_diarizationEnabled)
        {
            await _diarization.StopAsync();
        }
        
        _noiseCancellation.ResetNoiseProfile();
        _audioEnhancement.Reset();
        
        _isInputActive = false;
        _isOutputActive = false;
        _logger.LogInformation("All pipelines stopped");
    }

    /// <summary>
    /// Generate sign language gesture sequence from translated text.
    /// </summary>
    private void EmitSignLanguageGestures(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i].ToLowerInvariant().Trim('.', ',', '!', '?', ';', ':');
            _signLanguageGestures.OnNext(new SignLanguageGesture
            {
                Word = word,
                AnimationId = SignLanguageDictionary.GetAnimationId(word),
                DurationMs = Math.Max(300, word.Length * 100),
                SequenceIndex = i
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _processingEvents.Dispose();
        _subtitles.Dispose();
        _signLanguageGestures.Dispose();
    }
}

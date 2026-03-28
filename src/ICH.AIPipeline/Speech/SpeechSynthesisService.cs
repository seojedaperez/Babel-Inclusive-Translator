using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ICH.Shared.Configuration;

namespace ICH.AIPipeline.Speech;

/// <summary>
/// Text-to-speech synthesis using Azure Cognitive Services.
/// Converts translated text back into speech audio.
/// </summary>
public sealed class SpeechSynthesisService : IAsyncDisposable
{
    private readonly ILogger<SpeechSynthesisService> _logger;
    private readonly AzureSpeechSettings _settings;
    private SpeechSynthesizer? _synthesizer;

    /// <summary>
    /// Maps language codes to Azure neural voice names.
    /// </summary>
    private static readonly Dictionary<string, string> VoiceMap = new()
    {
        ["en"] = "en-US-JennyMultilingualNeural",
        ["en-US"] = "en-US-JennyMultilingualNeural",
        ["en-GB"] = "en-GB-SoniaNeural",
        ["es"] = "es-ES-ElviraNeural",
        ["es-ES"] = "es-ES-ElviraNeural",
        ["es-MX"] = "es-MX-DaliaNeural",
        ["fr"] = "fr-FR-DeniseNeural",
        ["fr-FR"] = "fr-FR-DeniseNeural",
        ["de"] = "de-DE-KatjaNeural",
        ["de-DE"] = "de-DE-KatjaNeural",
        ["it"] = "it-IT-ElsaNeural",
        ["pt"] = "pt-BR-FranciscaNeural",
        ["pt-BR"] = "pt-BR-FranciscaNeural",
        ["zh"] = "zh-CN-XiaoxiaoNeural",
        ["zh-CN"] = "zh-CN-XiaoxiaoNeural",
        ["ja"] = "ja-JP-NanamiNeural",
        ["ko"] = "ko-KR-SunHiNeural",
        ["ar"] = "ar-SA-ZariyahNeural",
        ["hi"] = "hi-IN-SwaraNeural",
        ["ru"] = "ru-RU-SvetlanaNeural"
    };

    public SpeechSynthesisService(
        ILogger<SpeechSynthesisService> logger,
        IOptions<AzureSpeechSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public string GetSubscriptionKey() => _settings.SubscriptionKey;
    public string GetRegion() => _settings.Region;

    /// <summary>
    /// Synthesize text to audio bytes (PCM 16kHz, 16-bit, mono).
    /// </summary>
    public async Task<SynthesisResult> SynthesizeToAudioAsync(
        string text,
        string language = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new SynthesisResult { Success = false, Error = "Empty text" };

        try
        {
            var speechConfig = SpeechConfig.FromSubscription(_settings.SubscriptionKey, _settings.Region);
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);

            var voiceName = GetVoiceName(language);
            speechConfig.SpeechSynthesisVoiceName = voiceName;

            using var synthesizer = new SpeechSynthesizer(speechConfig, null);
            var result = await synthesizer.SpeakTextAsync(text);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                _logger.LogDebug("Synthesized {Length} bytes for: {Text}",
                    result.AudioData.Length, text[..Math.Min(30, text.Length)]);

                return new SynthesisResult
                {
                    AudioData = result.AudioData,
                    Success = true,
                    DurationMs = result.AudioDuration.TotalMilliseconds,
                    VoiceName = voiceName
                };
            }

            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                _logger.LogError("TTS canceled: {Reason} - {Details}",
                    cancellation.Reason, cancellation.ErrorDetails);

                return new SynthesisResult
                {
                    Success = false,
                    Error = $"{cancellation.Reason}: {cancellation.ErrorDetails}"
                };
            }

            return new SynthesisResult { Success = false, Error = $"Unexpected result: {result.Reason}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech synthesis failed for text: {Text}", text[..Math.Min(50, text.Length)]);
            return new SynthesisResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Synthesize text using SSML for more control over prosody.
    /// </summary>
    public async Task<SynthesisResult> SynthesizeWithSsmlAsync(
        string text,
        string language,
        double rate = 1.0,
        double pitch = 0,
        CancellationToken ct = default)
    {
        var voiceName = GetVoiceName(language);
        var ssml = $@"
<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{language}'>
    <voice name='{voiceName}'>
        <prosody rate='{rate}' pitch='{(pitch >= 0 ? "+" : "")}{pitch}%'>
            {System.Security.SecurityElement.Escape(text)}
        </prosody>
    </voice>
</speak>";

        try
        {
            var speechConfig = SpeechConfig.FromSubscription(_settings.SubscriptionKey, _settings.Region);
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);

            using var synthesizer = new SpeechSynthesizer(speechConfig, null);
            var result = await synthesizer.SpeakSsmlAsync(ssml);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                return new SynthesisResult
                {
                    AudioData = result.AudioData,
                    Success = true,
                    DurationMs = result.AudioDuration.TotalMilliseconds,
                    VoiceName = voiceName
                };
            }

            return new SynthesisResult { Success = false, Error = $"SSML synthesis failed: {result.Reason}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSML synthesis failed");
            return new SynthesisResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Get the neural voice name for a language code.
    /// </summary>
    public static string GetVoiceName(string language)
    {
        if (VoiceMap.TryGetValue(language, out var voice))
            return voice;

        // Try base language (e.g., "en" from "en-AU")
        var baseLang = language.Split('-')[0];
        if (VoiceMap.TryGetValue(baseLang, out voice))
            return voice;

        return "en-US-JennyMultilingualNeural"; // Fallback
    }

    public ValueTask DisposeAsync()
    {
        _synthesizer?.Dispose();
        return ValueTask.CompletedTask;
    }
}

public record SynthesisResult
{
    public byte[] AudioData { get; init; } = Array.Empty<byte>();
    public bool Success { get; init; }
    public double DurationMs { get; init; }
    public string VoiceName { get; init; } = string.Empty;
    public string? Error { get; init; }
}

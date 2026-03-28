using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ICH.Shared.Configuration;

namespace ICH.AIPipeline.Speech;

/// <summary>
/// Accent conversion service using Azure Neural TTS with SSML customization.
/// Produces "accent-neutral" speech output while preserving natural voice quality.
/// Inspired by UTell.ai's Accent Conversion feature.
/// </summary>
public sealed class AccentConversionService
{
    private readonly ILogger<AccentConversionService> _logger;
    private readonly AzureSpeechSettings _settings;
    private bool _enabled = true;

    // Neutral voice options per language
    private static readonly Dictionary<string, AccentProfile> NeutralVoices = new()
    {
        ["en"] = new("en-US-JennyNeural", "en-US", "General"),
        ["en-US"] = new("en-US-JennyNeural", "en-US", "General"),
        ["en-GB"] = new("en-US-JennyNeural", "en-US", "General"), // Neutralize to US English
        ["en-IN"] = new("en-US-JennyNeural", "en-US", "General"), // Neutralize to US English
        ["es"] = new("es-MX-DaliaNeural", "es-MX", "General"),
        ["es-ES"] = new("es-MX-DaliaNeural", "es-MX", "General"),
        ["es-AR"] = new("es-MX-DaliaNeural", "es-MX", "General"),
        ["fr"] = new("fr-FR-DeniseNeural", "fr-FR", "General"),
        ["de"] = new("de-DE-KatjaNeural", "de-DE", "General"),
        ["pt"] = new("pt-BR-FranciscaNeural", "pt-BR", "General"),
        ["pt-BR"] = new("pt-BR-FranciscaNeural", "pt-BR", "General"),
        ["zh"] = new("zh-CN-XiaoxiaoNeural", "zh-CN", "General"),
        ["ja"] = new("ja-JP-NanamiNeural", "ja-JP", "General"),
        ["ko"] = new("ko-KR-SunHiNeural", "ko-KR", "General"),
        ["ar"] = new("ar-SA-ZariyahNeural", "ar-SA", "General"),
        ["hi"] = new("hi-IN-SwaraNeural", "hi-IN", "General"),
        ["ru"] = new("ru-RU-SvetlanaNeural", "ru-RU", "General"),
    };

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _logger.LogInformation("Accent conversion {Status}", value ? "enabled" : "disabled");
        }
    }

    public AccentConversionService(
        ILogger<AccentConversionService> logger,
        IOptions<AzureSpeechSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    /// <summary>
    /// Synthesize text with accent-neutral voice using SSML customization.
    /// Uses prosody and phoneme tags for clarity enhancement.
    /// </summary>
    public async Task<AccentConversionResult> ConvertToNeutralAccentAsync(
        string text,
        string targetLanguage,
        CancellationToken ct = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(text))
            return new AccentConversionResult { Success = false, AudioData = Array.Empty<byte>() };

        try
        {
            var profile = GetAccentProfile(targetLanguage);
            var speechConfig = SpeechConfig.FromSubscription(_settings.SubscriptionKey, _settings.Region);
            speechConfig.SpeechSynthesisVoiceName = profile.VoiceName;

            using var synthesizer = new SpeechSynthesizer(speechConfig, null);

            // Build SSML with prosody adjustments for clarity
            var ssml = BuildClaritySSML(text, profile);

            var result = await synthesizer.SpeakSsmlAsync(ssml);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                _logger.LogDebug("Accent conversion completed: {Bytes} bytes", result.AudioData.Length);
                return new AccentConversionResult
                {
                    Success = true,
                    AudioData = result.AudioData,
                    VoiceUsed = profile.VoiceName,
                    TargetLanguage = profile.Locale
                };
            }

            _logger.LogWarning("Accent conversion failed: {Reason}", result.Reason);
            return new AccentConversionResult { Success = false, AudioData = Array.Empty<byte>() };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Accent conversion failed for language {Language}", targetLanguage);
            return new AccentConversionResult { Success = false, AudioData = Array.Empty<byte>() };
        }
    }

    /// <summary>
    /// Build SSML with prosody adjustments optimized for accent clarity.
    /// </summary>
    private static string BuildClaritySSML(string text, AccentProfile profile)
    {
        // Use slightly slower rate and careful emphasis for maximum clarity
        return $@"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' 
                         xmlns:mstts='https://www.w3.org/2001/mstts' xml:lang='{profile.Locale}'>
            <voice name='{profile.VoiceName}'>
                <mstts:express-as style='{profile.Style}'>
                    <prosody rate='-5%' pitch='+0%' volume='+10%'>
                        {EscapeXml(text)}
                    </prosody>
                </mstts:express-as>
            </voice>
        </speak>";
    }

    private AccentProfile GetAccentProfile(string language)
    {
        // Try exact match first, then language code prefix
        if (NeutralVoices.TryGetValue(language, out var profile))
            return profile;

        var langPrefix = language.Split('-')[0];
        if (NeutralVoices.TryGetValue(langPrefix, out profile))
            return profile;

        // Default to US English
        _logger.LogWarning("No accent profile for '{Language}', defaulting to en-US", language);
        return NeutralVoices["en-US"];
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

public record AccentProfile(string VoiceName, string Locale, string Style);

public record AccentConversionResult
{
    public bool Success { get; init; }
    public byte[] AudioData { get; init; } = Array.Empty<byte>();
    public string VoiceUsed { get; init; } = string.Empty;
    public string TargetLanguage { get; init; } = string.Empty;
}

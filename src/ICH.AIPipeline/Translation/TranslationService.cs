using Azure;
using Azure.AI.Translation.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ICH.Shared.Configuration;
using System.Collections.Concurrent;

namespace ICH.AIPipeline.Translation;

/// <summary>
/// Real-time text translation using Azure Translator.
/// Supports auto-detection and caching for performance.
/// </summary>
public sealed class TranslationService
{
    private readonly ILogger<TranslationService> _logger;
    private readonly AzureTranslatorSettings _settings;
    private readonly TextTranslationClient _client;
    private readonly ConcurrentDictionary<string, string> _translationCache = new();
    private const int MaxCacheSize = 10000;

    public TranslationService(
        ILogger<TranslationService> logger,
        IOptions<AzureTranslatorSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        _client = new TextTranslationClient(
            new AzureKeyCredential(_settings.SubscriptionKey),
            new Uri(_settings.Endpoint),
            _settings.Region);
    }

    /// <summary>
    /// Translate text from source to target language.
    /// </summary>
    public async Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult { OriginalText = text, TranslatedText = text };

        // Check cache
        var cacheKey = $"{sourceLanguage ?? "auto"}:{targetLanguage}:{text}";
        if (_translationCache.TryGetValue(cacheKey, out var cached))
        {
            return new TranslationResult
            {
                OriginalText = text,
                TranslatedText = cached,
                SourceLanguage = sourceLanguage ?? "auto",
                TargetLanguage = targetLanguage,
                FromCache = true
            };
        }

        try
        {
            var targetLanguages = new[] { targetLanguage };

            Response<IReadOnlyList<TranslatedTextItem>> response;

            if (string.IsNullOrEmpty(sourceLanguage))
            {
                response = await _client.TranslateAsync(
                    targetLanguages: targetLanguages,
                    content: new[] { text },
                    cancellationToken: ct);
            }
            else
            {
                response = await _client.TranslateAsync(
                    targetLanguages: targetLanguages,
                    content: new[] { text },
                    sourceLanguage: sourceLanguage,
                    cancellationToken: ct);
            }

            var translation = response.Value.FirstOrDefault();
            if (translation == null)
            {
                _logger.LogWarning("No translation returned for text: {Text}", text[..Math.Min(50, text.Length)]);
                return new TranslationResult { OriginalText = text, TranslatedText = text };
            }

            var translatedText = translation.Translations.FirstOrDefault()?.Text ?? text;
            var detectedLanguage = translation.DetectedLanguage?.Language ?? sourceLanguage ?? "unknown";

            // Cache result
            if (_translationCache.Count < MaxCacheSize)
            {
                _translationCache.TryAdd(cacheKey, translatedText);
            }

            _logger.LogDebug("Translated [{Source}→{Target}]: {Original} → {Translated}",
                detectedLanguage, targetLanguage,
                text[..Math.Min(30, text.Length)],
                translatedText[..Math.Min(30, translatedText.Length)]);

            return new TranslationResult
            {
                OriginalText = text,
                TranslatedText = translatedText,
                SourceLanguage = detectedLanguage,
                TargetLanguage = targetLanguage,
                Confidence = translation.DetectedLanguage?.Confidence ?? 1.0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed for text: {Text}", text[..Math.Min(50, text.Length)]);
            return new TranslationResult
            {
                OriginalText = text,
                TranslatedText = text,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Batch translate multiple texts.
    /// </summary>
    public async Task<IReadOnlyList<TranslationResult>> TranslateBatchAsync(
        IEnumerable<string> texts,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken ct = default)
    {
        var results = new List<TranslationResult>();
        var textList = texts.ToList();

        try
        {
            var targetLanguages = new[] { targetLanguage };

            var response = await _client.TranslateAsync(
                targetLanguages: targetLanguages,
                content: textList,
                sourceLanguage: sourceLanguage,
                cancellationToken: ct);

            foreach (var item in response.Value)
            {
                var translated = item.Translations.FirstOrDefault()?.Text ?? string.Empty;
                results.Add(new TranslationResult
                {
                    OriginalText = textList[results.Count],
                    TranslatedText = translated,
                    SourceLanguage = item.DetectedLanguage?.Language ?? sourceLanguage ?? "unknown",
                    TargetLanguage = targetLanguage
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch translation failed");
            results.AddRange(textList.Select(t => new TranslationResult
            {
                OriginalText = t,
                TranslatedText = t,
                Error = ex.Message
            }));
        }

        return results;
    }

    /// <summary>
    /// Detect the language of given text.
    /// </summary>
    public async Task<string> DetectLanguageAsync(string text, CancellationToken ct = default)
    {
        try
        {
            var targetLanguages = new[] { "en" };
            var response = await _client.TranslateAsync(
                targetLanguages: targetLanguages,
                content: new[] { text },
                cancellationToken: ct);

            return response.Value.FirstOrDefault()?.DetectedLanguage?.Language ?? "unknown";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Language detection failed");
            return "unknown";
        }
    }

    /// <summary>
    /// Get supported languages.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetSupportedLanguagesAsync(
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetSupportedLanguagesAsync(cancellationToken: ct);
            return response.Value.Translation
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get supported languages");
            return new Dictionary<string, string>();
        }
    }

    public void ClearCache() => _translationCache.Clear();
}

public record TranslationResult
{
    public string OriginalText { get; init; } = string.Empty;
    public string TranslatedText { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = string.Empty;
    public string TargetLanguage { get; init; } = string.Empty;
    public double Confidence { get; init; } = 1.0;
    public bool FromCache { get; init; }
    public string? Error { get; init; }
}

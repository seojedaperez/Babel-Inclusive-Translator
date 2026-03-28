using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ICH.AIPipeline.Translation;

namespace ICH.API.Controllers;

/// <summary>
/// Azure Translator REST endpoint.
/// Provides real-time text translation using Azure Cognitive Services Translator.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class TranslateController : ControllerBase
{
    private readonly TranslationService _translator;
    private readonly ILogger<TranslateController> _logger;

    public TranslateController(TranslationService translator, ILogger<TranslateController> logger)
    {
        _translator = translator;
        _logger = logger;
    }

    /// <summary>
    /// Translate text from source to target language.
    /// POST /api/translate
    /// Body: { "text": "Hola mundo", "from": "es", "to": "en" }
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Translate([FromBody] TranslateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text is required" });

        _logger.LogInformation("Translation request: {From}→{To}, text={Text}",
            request.From ?? "auto", request.To,
            request.Text[..Math.Min(40, request.Text.Length)]);

        var result = await _translator.TranslateAsync(
            request.Text,
            request.To,
            string.IsNullOrEmpty(request.From) ? null : request.From,
            ct);

        if (!string.IsNullOrEmpty(result.Error))
        {
            _logger.LogError("Translation failed: {Error}", result.Error);
            return StatusCode(503, new
            {
                translatedText = request.Text, // fallback to original
                detectedLanguage = request.From ?? "unknown",
                error = result.Error
            });
        }

        return Ok(new
        {
            translatedText = result.TranslatedText,
            detectedLanguage = result.SourceLanguage,
            confidence = result.Confidence,
            fromCache = result.FromCache
        });
    }

    /// <summary>
    /// Get supported languages.
    /// GET /api/translate/languages
    /// </summary>
    [HttpGet("languages")]
    public async Task<IActionResult> GetLanguages(CancellationToken ct)
    {
        var languages = await _translator.GetSupportedLanguagesAsync(ct);
        return Ok(languages);
    }
}

public record TranslateRequest
{
    public string Text { get; init; } = string.Empty;
    public string? From { get; init; }
    public string To { get; init; } = "en";
}

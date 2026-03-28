using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ICH.AIPipeline.Speech;

namespace ICH.API.Controllers;

/// <summary>
/// Azure Speech Services REST endpoint.
/// Provides TTS synthesis returning WAV audio for client-side playback with setSinkId routing.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class SpeechController : ControllerBase
{
    private readonly SpeechSynthesisService _tts;
    private readonly ILogger<SpeechController> _logger;

    public SpeechController(SpeechSynthesisService tts, ILogger<SpeechController> logger)
    {
        _tts = tts;
        _logger = logger;
    }

    /// <summary>
    /// Synthesize text to WAV audio using Azure Neural TTS.
    /// POST /api/speech/synthesize
    /// Body: { "text": "Hello", "language": "en-US" }
    /// Returns: audio/wav binary
    /// </summary>
    [HttpPost("synthesize")]
    public async Task<IActionResult> Synthesize([FromBody] SynthesizeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text is required" });

        _logger.LogInformation("TTS request: lang={Lang}, text={Text}",
            request.Language, request.Text[..Math.Min(40, request.Text.Length)]);

        var result = await _tts.SynthesizeToAudioAsync(request.Text, request.Language, ct);

        if (!result.Success)
        {
            _logger.LogError("TTS failed: {Error}", result.Error);
            return StatusCode(503, new { error = result.Error });
        }

        // Build a proper WAV file from raw PCM data (16kHz, 16-bit, mono)
        var wavBytes = BuildWav(result.AudioData, 16000, 16, 1);

        return File(wavBytes, "audio/wav", $"tts-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.wav");
    }

    /// <summary>
    /// Build a WAV file header + PCM data.
    /// </summary>
    private static byte[] BuildWav(byte[] pcmData, int sampleRate, int bitsPerSample, int channels)
    {
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RIFF header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcmData.Length); // ChunkSize
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt sub-chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16); // Subchunk1Size (PCM)
        writer.Write((short)1); // AudioFormat (PCM)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        // data sub-chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        return ms.ToArray();
    }
    /// <summary>
    /// Get a short-lived Azure Speech token for browser-direct SDK usage.
    /// GET /api/speech/token
    /// Returns: { token, region }
    /// </summary>
    [HttpGet("token")]
    public async Task<IActionResult> GetToken()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key",
                _tts.GetSubscriptionKey());
            var region = _tts.GetRegion();
            var response = await client.PostAsync(
                $"https://{region}.api.cognitive.microsoft.com/sts/v1.0/issueToken", null);
    
            if (!response.IsSuccessStatusCode)
                return StatusCode(503, new { error = "Failed to get token" });
    
            var token = await response.Content.ReadAsStringAsync();
            return Ok(new { token, region });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token generation failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public record SynthesizeRequest
{
    public string Text { get; init; } = string.Empty;
    public string Language { get; init; } = "en-US";
}

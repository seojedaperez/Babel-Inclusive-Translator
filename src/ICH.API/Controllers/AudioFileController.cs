using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.AspNetCore.SignalR;
using ICH.API.Hubs;
using ICH.Shared.Hubs;
using ICH.Shared.Configuration;
using Microsoft.Extensions.Options;
using ICH.AIPipeline.Translation;

namespace ICH.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AudioFileController : ControllerBase
{
    private readonly AzureSpeechSettings _settings;
    private readonly IHubContext<AudioHub, IAudioHubClient> _hubContext;
    private readonly ILogger<AudioFileController> _logger;
    private readonly TranslationService _translationService;

    public AudioFileController(
        IOptions<AzureSpeechSettings> settings, 
        IHubContext<AudioHub, IAudioHubClient> hubContext,
        ILogger<AudioFileController> logger,
        TranslationService translationService)
    {
        _settings = settings.Value;
        _hubContext = hubContext;
        _logger = logger;
        _translationService = translationService;
    }

    [HttpPost("translate")]
    [RequestSizeLimit(104857600)] // 100MB limit for audio files
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> TranslateAudioFile(
        IFormFile file, 
        [FromForm] string sessionId, 
        [FromForm] string sourceLanguage = "en-US", 
        [FromForm] string targetLanguage = "en")
    {
        if (file == null || file.Length == 0)
            return BadRequest("No audio file provided.");

        var tempFilePath = Path.GetTempFileName();
        
        try
        {
            // 1. Save uploaded file to temp disk
            using (var stream = new FileStream(tempFilePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // 2. We run the translation in a background task so the HTTP request completes immediately
            _ = Task.Run(() => ProcessAudioFileAsync(tempFilePath, sessionId, sourceLanguage, targetLanguage));

            return Accepted(new { message = "Audio file accepted for processing.", sessionId = sessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate file translation.");
            return StatusCode(500, "Internal Server Error during translation initiating.");
        }
    }

    private async Task ProcessAudioFileAsync(string tempFilePath, string sessionId, string sourceLanguage, string targetLanguage)
    {
        try
        {
            var speechConfig = SpeechConfig.FromSubscription(_settings.SubscriptionKey, _settings.Region);
            speechConfig.SpeechRecognitionLanguage = sourceLanguage;
            // Optionally enable diarization for file uploads
            speechConfig.SetProperty("DiarizationEnabled", "true");
            speechConfig.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");

            // Azure SDK natively parses WAV files
            using var audioConfig = AudioConfig.FromWavFileInput(tempFilePath);
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            
            var taskCompletionSource = new TaskCompletionSource<int>();

            recognizer.Recognized += async (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    _logger.LogInformation($"[File Transcribed] {e.Result.Text}");

                    string translatedText = e.Result.Text;
                    
                    // Translate if target language differs
                    if (sourceLanguage.Split('-')[0] != targetLanguage.Split('-')[0])
                    {
                        var tRes = await _translationService.TranslateAsync(e.Result.Text, targetLanguage, sourceLanguage);
                        translatedText = tRes.TranslatedText;
                    }

                    // Extract Speaker ID for Diarization
                    string? speakerId = null;
                    try {
                        var json = e.Result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
                        if (!string.IsNullOrEmpty(json) && json.Contains("SpeakerId"))
                        {
                            var start = json.IndexOf("\"SpeakerId\":\"") + 13;
                            var end = json.IndexOf("\"", start);
                            if (start > 12 && end > start) speakerId = json.Substring(start, end - start);
                        }
                    } catch { }

                    // Send Final Transcript to UI
                    await _hubContext.Clients.Group(sessionId).ReceiveTranscript(
                        sessionId, 
                        e.Result.Text, 
                        translatedText, 
                        sourceLanguage, 
                        targetLanguage,
                        speakerId ?? "Guest", 
                        1.0, 
                        "Neutral"
                    );

                    // Also emulate Sign language cues for the browser to animate
                    var words = translatedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < words.Length; i++)
                    {
                        var word = words[i].ToLowerInvariant().Trim('.', ',', '!', '?', ';', ':');
                        await _hubContext.Clients.Group(sessionId).ReceiveSignLanguageGesture(
                            sessionId, word, "sign_" + word, Math.Max(300, word.Length * 100), i
                        );
                        await Task.Delay(200); // add microscopic pacing so the avatar fluidly reacts
                    }
                }
            };

            recognizer.Canceled += (s, e) =>
            {
                _logger.LogWarning($"Canceled audio file processing: {e.Reason}");
                taskCompletionSource.TrySetResult(0);
            };

            recognizer.SessionStopped += (s, e) =>
            {
                _logger.LogInformation("Audio file session stopped.");
                taskCompletionSource.TrySetResult(0);
            };

            await recognizer.StartContinuousRecognitionAsync();
            await taskCompletionSource.Task; // wait until End of File
            await recognizer.StopContinuousRecognitionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Audio File");
        }
        finally
        {
            if (System.IO.File.Exists(tempFilePath))
            {
                System.IO.File.Delete(tempFilePath);
            }
        }
    }
}

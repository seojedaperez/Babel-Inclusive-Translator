using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ICH.AIPipeline.Copilot;
using ICH.Domain.Interfaces;
using ICH.Shared.DTOs;

namespace ICH.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CopilotController : ControllerBase
{
    private readonly CopilotService _copilotService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CopilotController> _logger;

    public CopilotController(
        CopilotService copilotService,
        IUnitOfWork unitOfWork,
        ILogger<CopilotController> logger)
    {
        _copilotService = copilotService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Ask a question about a session.
    /// </summary>
    [HttpPost("ask")]
    public async Task<ActionResult<CopilotResponse>> Ask([FromBody] CopilotRequest request, CancellationToken ct)
    {
        var session = await _unitOfWork.Sessions.GetByIdAsync(request.SessionId, ct);
        if (session == null) return NotFound("Session not found");

        var transcripts = await _unitOfWork.Transcripts.GetBySessionIdAsync(request.SessionId, ct);
        var transcriptDtos = transcripts.Select(t => new TranscriptEntryDto
        {
            Id = t.Id,
            OriginalText = t.OriginalText,
            TranslatedText = t.TranslatedText,
            SourceLanguage = t.SourceLanguage,
            TargetLanguage = t.TargetLanguage,
            Timestamp = t.Timestamp,
            SpeakerId = t.SpeakerId,
            SpeakerName = t.SpeakerName,
            Confidence = t.Confidence,
            Emotion = t.Emotion
        }).ToList();

        var response = await _copilotService.AskAsync(request, transcriptDtos, ct);
        return Ok(response);
    }

    /// <summary>
    /// Ask a question directly passing the transcript context in memory.
    /// </summary>
    [HttpPost("ask-direct")]
    [AllowAnonymous]
    public async Task<ActionResult<CopilotResponse>> AskDirect([FromBody] DirectCopilotRequest request, CancellationToken ct)
    {
        // Process query through the AI analysis engine
        try
        {
            var copilotReq = new CopilotRequest { Query = request.Query, SessionId = Guid.NewGuid() };
            var response = await _copilotService.AskAsync(copilotReq, request.Transcripts, ct);
            
            // Check if the service returned its own error message
            if (response.Answer != null && !response.Answer.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary engine unavailable, using secondary analysis");
        }

        // Secondary analysis: keyword search over provided transcripts
        var query = request.Query?.ToLowerInvariant() ?? "";
        var matches = request.Transcripts
            .Where(t => (t.OriginalText ?? "").ToLowerInvariant().Contains(query) ||
                        (t.TranslatedText ?? "").ToLowerInvariant().Contains(query) ||
                        (t.SpeakerName ?? "").ToLowerInvariant().Contains(query))
            .ToList();

        string answer;
        if (matches.Any())
        {
            var lines = matches.Select(m =>
                $"• [{m.Timestamp:HH:mm:ss}] {m.SpeakerName ?? "Unknown"}: {m.OriginalText}");
            answer = $"Found {matches.Count} transcript(s) matching \"{request.Query}\":\n{string.Join("\n", lines)}";
        }
        else if (request.Transcripts.Any())
        {
            answer = $"No matches found for \"{request.Query}\" in {request.Transcripts.Count} transcript(s). Try different keywords.";
        }
        else
        {
            answer = "No transcripts available yet. Start a session and record some conversation first.";
        }

        return Ok(new CopilotResponse { Answer = answer, ActionItems = new List<string>() });
    }

    /// <summary>
    /// Generate a session summary.
    /// </summary>
    [HttpPost("{sessionId:guid}/summary")]
    public async Task<ActionResult<string>> GenerateSummary(Guid sessionId, CancellationToken ct)
    {
        var session = await _unitOfWork.Sessions.GetByIdAsync(sessionId, ct);
        if (session == null) return NotFound();

        var transcripts = await _unitOfWork.Transcripts.GetBySessionIdAsync(sessionId, ct);
        var transcriptDtos = transcripts.Select(t => new TranscriptEntryDto
        {
            Id = t.Id,
            OriginalText = t.OriginalText,
            TranslatedText = t.TranslatedText,
            SourceLanguage = t.SourceLanguage,
            TargetLanguage = t.TargetLanguage,
            Timestamp = t.Timestamp,
            SpeakerId = t.SpeakerId,
            SpeakerName = t.SpeakerName,
            Confidence = t.Confidence,
            Emotion = t.Emotion
        }).ToList();

        var summary = await _copilotService.GenerateSummaryAsync(transcriptDtos, ct);

        // Save summary to session
        session.Summary = summary;
        await _unitOfWork.Sessions.UpdateAsync(session, ct);

        return Ok(new { summary });
    }

    /// <summary>
    /// Extract action items from a session.
    /// </summary>
    [HttpGet("{sessionId:guid}/action-items")]
    public async Task<ActionResult<List<string>>> GetActionItems(Guid sessionId, CancellationToken ct)
    {
        var transcripts = await _unitOfWork.Transcripts.GetBySessionIdAsync(sessionId, ct);
        var transcriptDtos = transcripts.Select(t => new TranscriptEntryDto
        {
            Id = t.Id,
            OriginalText = t.OriginalText,
            TranslatedText = t.TranslatedText,
            SourceLanguage = t.SourceLanguage,
            TargetLanguage = t.TargetLanguage,
            Timestamp = t.Timestamp,
            SpeakerId = t.SpeakerId,
            SpeakerName = t.SpeakerName,
            Confidence = t.Confidence,
            Emotion = t.Emotion
        }).ToList();

        var items = await _copilotService.ExtractActionItemsAsync(transcriptDtos, ct);
        return Ok(items);
    }
}

public class DirectCopilotRequest
{
    public string Query { get; set; } = string.Empty;
    public List<TranscriptEntryDto> Transcripts { get; set; } = new();
}

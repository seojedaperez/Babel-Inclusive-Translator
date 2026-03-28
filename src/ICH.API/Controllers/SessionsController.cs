using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ICH.Domain.Interfaces;
using ICH.Domain.Entities;
using ICH.Shared.DTOs;

namespace ICH.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class SessionsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        IUnitOfWork unitOfWork,
        IBlobStorageService blobStorage,
        ILogger<SessionsController> logger)
    {
        _unitOfWork = unitOfWork;
        _blobStorage = blobStorage;
        _logger = logger;
    }

    /// <summary>
    /// Get all sessions for the current user.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> GetSessions(CancellationToken ct)
    {
        var userId = GetUserId();
        var sessions = await _unitOfWork.Sessions.GetByUserIdAsync(userId, ct);

        var dtos = sessions.Select(s => MapToDto(s)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Get a specific session with transcripts.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SessionDto>> GetSession(Guid id, CancellationToken ct)
    {
        var session = await _unitOfWork.Sessions.GetByIdWithTranscriptsAsync(id, ct);
        if (session == null) return NotFound();

        if (session.UserId != GetUserId())
            return Forbid();

        return Ok(MapToDto(session));
    }

    /// <summary>
    /// Create a new session.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SessionDto>> CreateSession([FromBody] CreateSessionRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            var anonUser = await _unitOfWork.Users.GetByEmailAsync("anon@ich.local", ct);
            if (anonUser == null)
            {
                anonUser = new User { Id = Guid.NewGuid(), Email = "anon@ich.local", DisplayName = "Anonymous", PreferredLanguage = "en" };
                await _unitOfWork.Users.CreateAsync(anonUser, ct);
            }
            userId = anonUser.Id;
        }

        var session = new Session
        {
            Title = request.Title,
            UserId = userId,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            ConsentGiven = request.ConsentGiven,
            IsRecordingEnabled = request.EnableRecording
        };

        session.Start();
        var created = await _unitOfWork.Sessions.CreateAsync(session, ct);

        _logger.LogInformation("Session created: {SessionId}", created.Id);
        return CreatedAtAction(nameof(GetSession), new { id = created.Id }, MapToDto(created));
    }

    /// <summary>
    /// Complete a session.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult> CompleteSession(Guid id, CancellationToken ct)
    {
        var session = await _unitOfWork.Sessions.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        var currentUserId = GetUserId();
        if (currentUserId != Guid.Empty && session.UserId != currentUserId) return Forbid();

        session.Complete();
        await _unitOfWork.Sessions.UpdateAsync(session, ct);

        return Ok();
    }

    /// <summary>
    /// Delete a session and its data.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteSession(Guid id, CancellationToken ct)
    {
        var session = await _unitOfWork.Sessions.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        var currentUserId = GetUserId();
        if (currentUserId != Guid.Empty && session.UserId != currentUserId) return Forbid();

        // Delete audio recording
        await _blobStorage.DeleteAudioAsync(id, ct);
        // Delete transcripts
        await _unitOfWork.Transcripts.DeleteBySessionIdAsync(id, ct);
        // Delete session
        await _unitOfWork.Sessions.DeleteAsync(id, ct);

        _logger.LogInformation("Session deleted: {SessionId}", id);
        return NoContent();
    }

    /// <summary>
    /// Get recording URL for a session.
    /// </summary>
    [HttpGet("{id:guid}/recording")]
    public async Task<ActionResult<string>> GetRecordingUrl(Guid id, CancellationToken ct)
    {
        var session = await _unitOfWork.Sessions.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        var currentUserId = GetUserId();
        if (currentUserId != Guid.Empty && session.UserId != currentUserId) return Forbid();

        var url = await _blobStorage.GetAudioUrlAsync(id, TimeSpan.FromHours(1), ct);
        if (string.IsNullOrEmpty(url)) return NotFound("No recording available");

        return Ok(new { url });
    }

    /// <summary>
    /// Get transcripts for a session.
    /// </summary>
    [HttpGet("{id:guid}/transcripts")]
    public async Task<ActionResult<IReadOnlyList<TranscriptEntryDto>>> GetTranscripts(Guid id, CancellationToken ct)
    {
        var session = await _unitOfWork.Sessions.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        var currentUserId = GetUserId();
        if (currentUserId != Guid.Empty && session.UserId != currentUserId) return Forbid();

        var entries = await _unitOfWork.Transcripts.GetBySessionIdAsync(id, ct);

        var dtos = entries.Select(e => new TranscriptEntryDto
        {
            Id = e.Id,
            OriginalText = e.OriginalText,
            TranslatedText = e.TranslatedText,
            SourceLanguage = e.SourceLanguage,
            TargetLanguage = e.TargetLanguage,
            Timestamp = e.Timestamp,
            SpeakerId = e.SpeakerId,
            SpeakerName = e.SpeakerName,
            Confidence = e.Confidence,
            Emotion = e.Emotion
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Update session summary.
    /// </summary>
    [HttpPut("{id:guid}/summary")]
    public async Task<ActionResult> UpdateSummary(Guid id, [FromBody] UpdateSummaryRequest request, CancellationToken ct)
    {
        var session = await _unitOfWork.Sessions.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        var currentUserId = GetUserId();
        if (currentUserId != Guid.Empty && session.UserId != currentUserId) return Forbid();

        session.Summary = request.Summary;
        await _unitOfWork.Sessions.UpdateAsync(session, ct);

        return Ok();
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private static SessionDto MapToDto(Session s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        UserId = s.UserId.ToString(),
        StartedAt = s.StartedAt,
        EndedAt = s.EndedAt,
        SourceLanguage = s.SourceLanguage,
        TargetLanguage = s.TargetLanguage,
        Status = (Shared.DTOs.SessionStatus)(int)s.Status,
        Summary = s.Summary,
        RecordingUrl = s.RecordingBlobUrl,
        Transcripts = s.TranscriptEntries?.Select(t => new TranscriptEntryDto
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
        }).ToList() ?? []
    };

    /// <summary>
    /// Archive a transcript log to Azure Blob Storage.
    /// POST /api/sessions/archive
    /// </summary>
    [HttpPost("archive")]
    public async Task<IActionResult> ArchiveTranscript([FromBody] ArchiveTranscriptRequest request, CancellationToken ct)
    {
        try
        {
            var blobName = $"transcripts/{request.SessionTitle ?? "session"}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.json";
            var jsonContent = System.Text.Json.JsonSerializer.Serialize(new
            {
                title = request.SessionTitle,
                archivedAt = DateTimeOffset.UtcNow,
                sourceLanguage = request.SourceLanguage,
                targetLanguage = request.TargetLanguage,
                entryCount = request.Entries?.Count ?? 0,
                entries = request.Entries
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent));
            var url = await _blobStorage.UploadAudioAsync(
                Guid.NewGuid(), stream, "application/json", ct);

            _logger.LogInformation("Transcript archived to blob: {BlobUrl}", url);
            return Ok(new { url, blobName, entryCount = request.Entries?.Count ?? 0 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive transcript");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public record CreateSessionRequest
{
    public string Title { get; init; } = string.Empty;
    public string SourceLanguage { get; init; } = "en-US";
    public string TargetLanguage { get; init; } = "es";
    public bool ConsentGiven { get; init; }
    public bool EnableRecording { get; init; }
}

public record UpdateSummaryRequest
{
    public string Summary { get; init; } = string.Empty;
}

public record ArchiveTranscriptRequest
{
    public string? SessionTitle { get; init; }
    public string? SourceLanguage { get; init; }
    public string? TargetLanguage { get; init; }
    public List<ArchiveTranscriptEntry>? Entries { get; init; }
}

public record ArchiveTranscriptEntry
{
    public string? Time { get; init; }
    public string? Text { get; init; }
    public double Confidence { get; init; }
    public string? User { get; init; }
    public string? Modality { get; init; }
}

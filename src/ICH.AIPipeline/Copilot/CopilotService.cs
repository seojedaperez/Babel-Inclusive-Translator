using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ICH.Shared.Configuration;
using ICH.Shared.DTOs;
using System.Text;

namespace ICH.AIPipeline.Copilot;

/// <summary>
/// AI Copilot — Intelligent Transcript Analysis Engine.
/// Uses local NLP heuristics for keyword extraction, speaker attribution,
/// timeline analysis, and structured response generation.
/// Automatically upgrades to Azure OpenAI when a valid key is configured.
/// </summary>
public sealed class CopilotService
{
    private readonly ILogger<CopilotService> _logger;
    private readonly AzureOpenAISettings _settings;

    public CopilotService(
        ILogger<CopilotService> logger,
        IOptions<AzureOpenAISettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<CopilotResponse> AskAsync(
        CopilotRequest request,
        IReadOnlyList<TranscriptEntryDto> transcripts,
        CancellationToken ct = default)
    {
        // Route through Azure OpenAI when the deployment is active
        if (!string.IsNullOrEmpty(_settings.ApiKey) &&
            _settings.ApiKey.Length > 10 &&
            !string.IsNullOrEmpty(_settings.Endpoint) &&
            _settings.Endpoint.StartsWith("https://"))
        {
            try
            {
                return await AskAzureOpenAIAsync(request, transcripts, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cloud AI engine timeout — switching to edge processing");
            }
        }

        // Edge NLP analysis engine
        return await Task.FromResult(AnalyzeLocally(request.Query, transcripts));
    }

    public async Task<string> GenerateSummaryAsync(
        IReadOnlyList<TranscriptEntryDto> transcripts,
        CancellationToken ct = default)
    {
        if (!transcripts.Any())
            return "No transcript entries available for this session.";

        var speakers = transcripts
            .Select(t => t.SpeakerName ?? t.SpeakerId ?? "Unknown")
            .Distinct().ToList();

        var sb = new StringBuilder();
        sb.AppendLine("## Session Summary");
        sb.AppendLine($"**Participants ({speakers.Count}):** {string.Join(", ", speakers)}");
        sb.AppendLine($"**Total Entries:** {transcripts.Count}");
        sb.AppendLine();

        foreach (var t in transcripts.Take(20))
        {
            var speaker = t.SpeakerName ?? "Unknown";
            sb.AppendLine($"- [{t.Timestamp:HH:mm:ss}] **{speaker}**: {t.OriginalText}");
        }
        if (transcripts.Count > 20)
            sb.AppendLine($"- ... and {transcripts.Count - 20} more entries");

        return await Task.FromResult(sb.ToString());
    }

    public async Task<List<string>> ExtractActionItemsAsync(
        IReadOnlyList<TranscriptEntryDto> transcripts,
        CancellationToken ct = default)
    {
        var actionKeywords = new[] { "need to", "should", "will", "must", "todo", "action", "follow up", "deadline", "task" };
        return await Task.FromResult(transcripts
            .Where(t => actionKeywords.Any(k => (t.OriginalText ?? "").Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Select(t => $"[{t.Timestamp:HH:mm}] {t.SpeakerName ?? "Unknown"}: {t.OriginalText}")
            .ToList());
    }

    private CopilotResponse AnalyzeLocally(string query, IReadOnlyList<TranscriptEntryDto> transcripts)
    {
        if (!transcripts.Any())
        {
            return new CopilotResponse
            {
                Answer = "No transcripts in the current session yet. Start a conversation first, then ask me questions about what was discussed.",
                ActionItems = new List<string>()
            };
        }

        var queryLower = query.ToLowerInvariant();
        var stopWords = new HashSet<string> { "the", "what", "was", "were", "that", "this", "about", "from", "with", "how", "who", "when", "donde", "cual", "como", "que", "las", "los", "del", "para", "por", "una", "uno", "hablo", "dijo", "said", "sobre", "can", "you", "tell", "me" };

        var words = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .ToArray();

        // Score each transcript by keyword relevance
        var scored = transcripts
            .Select(t =>
            {
                var text = (t.OriginalText ?? "").ToLowerInvariant();
                var translated = (t.TranslatedText ?? "").ToLowerInvariant();
                int score = words.Count(w => text.Contains(w) || translated.Contains(w));
                if (text.Contains(queryLower) || translated.Contains(queryLower))
                    score += 10;
                return new { Transcript = t, Score = score };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        var sb = new StringBuilder();

        if (scored.Any())
        {
            sb.AppendLine($"Found **{scored.Count}** relevant transcript(s) matching your query:\n");

            foreach (var item in scored.Take(8))
            {
                var t = item.Transcript;
                var speaker = t.SpeakerName ?? t.SpeakerId ?? "Unknown";
                sb.AppendLine($"- **[{t.Timestamp:HH:mm:ss}] {speaker}:** {t.OriginalText}");
                if (!string.IsNullOrEmpty(t.TranslatedText) && t.TranslatedText != t.OriginalText)
                    sb.AppendLine($"  Translation: {t.TranslatedText}");
            }

            if (scored.Count > 8)
                sb.AppendLine($"\n... and {scored.Count - 8} more matches.");

            var speakerGroups = scored.GroupBy(s => s.Transcript.SpeakerName ?? "Unknown");
            sb.AppendLine($"\n**Speakers involved:** {string.Join(", ", speakerGroups.Select(g => $"{g.Key} ({g.Count()} mentions)"))}");
        }
        else
        {
            var speakers = transcripts.Select(t => t.SpeakerName ?? "Unknown").Distinct().ToList();

            sb.AppendLine($"No exact matches for \"{query}\" in {transcripts.Count} transcript(s).\n");
            sb.AppendLine("**Session Overview:**");
            sb.AppendLine($"- **Entries:** {transcripts.Count}");
            sb.AppendLine($"- **Speakers:** {string.Join(", ", speakers)}");

            var first = transcripts.First();
            var last = transcripts.Last();
            var firstText = (first.OriginalText ?? "").Length > 80 ? (first.OriginalText ?? "")[..80] + "..." : first.OriginalText;
            var lastText = (last.OriginalText ?? "").Length > 80 ? (last.OriginalText ?? "")[..80] + "..." : last.OriginalText;

            sb.AppendLine($"- **First entry:** [{first.Timestamp:HH:mm:ss}] {firstText}");
            sb.AppendLine($"- **Last entry:** [{last.Timestamp:HH:mm:ss}] {lastText}");
            sb.AppendLine("\nTry searching with different keywords or ask about a specific speaker.");
        }

        return new CopilotResponse
        {
            Answer = sb.ToString(),
            ActionItems = new List<string>()
        };
    }

    private async Task<CopilotResponse> AskAzureOpenAIAsync(
        CopilotRequest request,
        IReadOnlyList<TranscriptEntryDto> transcripts,
        CancellationToken ct)
    {
        var client = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(_settings.Endpoint),
            new System.ClientModel.ApiKeyCredential(_settings.ApiKey));
        var chatClient = client.GetChatClient(_settings.DeploymentName);

        var context = string.Join("\n", transcripts.Select(t =>
            $"[{t.Timestamp:HH:mm:ss}] {t.SpeakerName ?? "Unknown"}: {t.OriginalText}"));

        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            new OpenAI.Chat.SystemChatMessage("You are an AI assistant for the Inclusive Communication Hub. Answer questions about transcripts concisely."),
            new OpenAI.Chat.UserChatMessage($"Transcripts:\n{context}\n\nQuestion: {request.Query}")
        };

        var options = new OpenAI.Chat.ChatCompletionOptions { MaxOutputTokenCount = 2000, Temperature = 0.3f };
        var completion = await chatClient.CompleteChatAsync(messages, options, ct);

        return new CopilotResponse
        {
            Answer = completion.Value.Content[0].Text,
            ActionItems = new List<string>()
        };
    }
}

using System.Text.Json;
using AgenticHrmApi.Contracts;
using AgenticHrmApi.Data;
using System.Security.Claims;

namespace AgenticHrmApi.Services;

public class ConversationService(
    AppDbContext db,
    IntentRouter router,
    IReasoner reasoner,
    IClock clock,
    ILogger<ConversationService> log)
{
    /// 12 was sized for HR commands only. Open-domain answering makes a
    /// legitimate session much longer.
    public const int MaxTurns = 24;
    public const int MaxReplyChars = 200;
    public const int MaxHistoryTurns = 16;
    public const int MaxSlotReasks = 2;
    public const int MaxConsecutiveEmpty = 2;
    public const string EmptyTranscriptReply = "Sorry, I didn't catch that.";

    private static readonly JsonSerializerOptions Json =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<ConverseResponse> ProcessAsync(ConverseRequest req, ClaimsPrincipal? principal = null, CancellationToken ct = default)
    {
        var history = Deserialize<List<ConversationTurn>>(req.History) ?? [];
        var pending = Deserialize<PendingAction>(req.PendingAction);

        if (history.Count > MaxTurns)
            return Close("We've gone in circles — say Kuddus to start again.");

        var user = await db.Users.FindAsync([req.UserId], ct);
        if (user is null)
        {
            log.LogWarning("Converse for unknown user {UserId}.", req.UserId);
            return Close("I couldn't find your account.");
        }

        var transcript = Combine(req.BargeInPrefix, req.Text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            // Stateless: derive the streak from history rather than a field.
            var streak = TrailingEmptyReplies(history) + 1;
            if (streak >= MaxConsecutiveEmpty)
                return Close("I'll be here if you need me.");

            return new ConverseResponse
            {
                Reply = EmptyTranscriptReply,
                Transcript = string.Empty,
                ConversationOpen = true,
                PendingAction = pending
            };
        }

        try
        {
            var reasoning = await reasoner.ReasonAsync(new ReasoningInput
            {
                User = user,
                Utterance = transcript,
                History = history.TakeLast(MaxHistoryTurns).ToList(),
                Pending = pending,
                Today = clock.UtcNow.Date
            }, ct);

            var result = await router.RouteAsync(new IntentContext
            {
                User = user,
                Principal = principal,
                Intent = reasoning.Intent,
                Slots = Merge(pending?.Slots, reasoning.Slots),
                Pending = pending,
                History = history,
                Transcript = transcript,
                ProposedReply = reasoning.Reply
            }, ct);

            // Spec 10.5: an answer that filled no new slot is an unusable answer.
            if (result.Pending?.Kind == "collectingSlots" && pending?.Kind == "collectingSlots")
            {
                var progressed = result.Pending.Slots.Count > pending.Slots.Count;
                var attempts = progressed ? 0 : pending.Attempts + 1;

                if (attempts > MaxSlotReasks)
                    return Close("Let's try that again later.");

                result.Pending.Attempts = attempts;
            }

            return new ConverseResponse
            {
                Reply = Cap(result.Reply, result.MaxReplyChars),
                Transcript = transcript,
                Intent = reasoning.Intent,
                ConversationOpen = result.ConversationOpen,
                DidAct = result.DidAct,
                PendingAction = result.Pending
            };
        }
        catch (Exception ex)
        {
            // Never speak ex.Message — the client reads the reply aloud.
            log.LogError(ex, "Conversation turn failed for user {UserId}.", req.UserId);
            return new ConverseResponse
            {
                Reply = "Something went wrong on my side. Try again?",
                Transcript = transcript,
                ConversationOpen = true
            };
        }
    }

    /// Counts Kuddus turns at the END of the history that were
    /// "didn't catch that" replies. Any understood turn breaks the streak.
    private static int TrailingEmptyReplies(List<ConversationTurn> history)
    {
        var count = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role != "kuddus") continue;
            if (history[i].Text == EmptyTranscriptReply) count++;
            else break;
        }
        return count;
    }

    /// Slots already collected survive; the new turn's values win on conflict.
    private static Dictionary<string, string> Merge(
        Dictionary<string, string>? existing, Dictionary<string, string> incoming)
    {
        var merged = existing is null ? new() : new Dictionary<string, string>(existing);
        foreach (var kv in incoming) merged[kv.Key] = kv.Value;
        return merged;
    }

    private static string Combine(string? prefix, string text) =>
        string.IsNullOrWhiteSpace(prefix) ? text.Trim() : $"{prefix.Trim()} {text.Trim()}".Trim();

    /// Trims a reply to its spoken length budget.
    ///
    /// The reply is read aloud, so where it stops matters. A plain slice
    /// produced things like "...which the plant use" mid-word, which sounds
    /// broken. Prefer the last complete sentence inside the budget, then a
    /// word boundary, and only hard-cut when the text offers neither.
    ///
    /// Public because it is a pure string function with no state, and the
    /// per-result cap is worth testing directly.
    public static string Cap(string s, int maxChars)
    {
        if (s.Length <= maxChars) return s;

        var window = s[..maxChars];

        var sentenceEnd = window.LastIndexOfAny(['.', '!', '?']);
        if (sentenceEnd > 0) return window[..(sentenceEnd + 1)].TrimEnd();

        var wordEnd = window.LastIndexOf(' ');
        if (wordEnd > 0) return window[..wordEnd].TrimEnd();

        return window.TrimEnd();
    }

    private static ConverseResponse Close(string reply) =>
        new() { Reply = reply, ConversationOpen = false };

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch { return null; }
    }
}

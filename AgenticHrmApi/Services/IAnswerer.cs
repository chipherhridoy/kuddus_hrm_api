using AgenticHrmApi.Contracts;
using AgenticHrmApi.Models;

namespace AgenticHrmApi.Services;

public class AnswerInput
{
    public required User User { get; init; }
    public required string Utterance { get; init; }
    public IReadOnlyList<ConversationTurn> History { get; init; } = [];
    public DateTime Today { get; init; }
}

/// Open-domain answering, kept separate from IReasoner on purpose.
/// IReasoner does structured extraction at temperature 0 in JSON mode;
/// this generates prose with search grounding, and the two cannot be the
/// same call — Gemini rejects the googleSearch tool combined with
/// responseMimeType application/json (spec D2, verified 2026-08-31).
public interface IAnswerer
{
    /// Null on any failure — no key, disabled, HTTP error, unparseable
    /// response. The caller must always have a fallback reply.
    Task<string?> AnswerAsync(AnswerInput input, CancellationToken ct = default);
}

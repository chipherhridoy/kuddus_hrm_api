using AgenticHrmApi.Contracts;
using AgenticHrmApi.Models;

namespace AgenticHrmApi.Services;

public class ReasoningInput
{
    public required User User { get; init; }
    public required string Utterance { get; init; }
    public IReadOnlyList<ConversationTurn> History { get; init; } = [];
    public PendingAction? Pending { get; init; }
    public DateTime Today { get; init; }
}

public class ReasoningResult
{
    public string Intent { get; init; } = "chat";
    public Dictionary<string, string> Slots { get; init; } = new();
    public string? Reply { get; init; }
}

public interface IReasoner
{
    Task<ReasoningResult> ReasonAsync(ReasoningInput input, CancellationToken ct = default);
}

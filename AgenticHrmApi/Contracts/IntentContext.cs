using AgenticHrmApi.Models;
using System.Security.Claims;

namespace AgenticHrmApi.Contracts;

public class IntentContext
{
    public required User User { get; init; }
    public ClaimsPrincipal? Principal { get; init; }
    public required string Intent { get; init; }
    public Dictionary<string, string> Slots { get; init; } = new();
    public PendingAction? Pending { get; init; }
    public IReadOnlyList<ConversationTurn> History { get; init; } = [];
    public string Transcript { get; init; } = string.Empty;
    public string? ProposedReply { get; init; }   // Gemini's wording, when allowed
}

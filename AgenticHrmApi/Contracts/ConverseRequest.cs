using Microsoft.AspNetCore.Http;

namespace AgenticHrmApi.Contracts;

public class ConverseRequest
{
    public IFormFile? Audio { get; set; }
    public string? Text { get; set; }
    public int UserId { get; set; }
    public string? History { get; set; }         // JSON array of ConversationTurn
    public string? BargeInPrefix { get; set; }
    public string? PendingAction { get; set; }   // JSON PendingAction
}

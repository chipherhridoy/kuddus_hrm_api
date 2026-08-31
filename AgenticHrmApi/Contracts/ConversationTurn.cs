namespace AgenticHrmApi.Contracts;

public class ConversationTurn
{
    public string Role { get; set; } = "user";   // "user" | "kuddus"
    public string Text { get; set; } = string.Empty;
    public bool Truncated { get; set; }
}

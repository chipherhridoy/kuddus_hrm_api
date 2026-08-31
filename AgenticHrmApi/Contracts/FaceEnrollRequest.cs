namespace AgenticHrmApi.Contracts;

public class FaceEnrollRequest
{
    public int UserId { get; set; }
    public List<FaceCapture> Captures { get; set; } = new();
}

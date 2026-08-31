namespace AgenticHrmApi.Contracts;

public class LivenessStep
{
    public string Action { get; set; } = string.Empty;
    public string CropBase64 { get; set; } = string.Empty;
    public float Evidence { get; set; }
    public long TimestampMs { get; set; }
}

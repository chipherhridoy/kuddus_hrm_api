namespace AgenticHrmApi.Contracts;

public class FaceCapture
{
    public string Pose { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

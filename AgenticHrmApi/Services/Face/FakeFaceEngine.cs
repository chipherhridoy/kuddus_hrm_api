namespace AgenticHrmApi.Services.Face;

public class FakeFaceEngine : IFaceEngine
{
    private readonly Dictionary<string, float[]> _embeddings;
    private readonly Dictionary<string, DetectedFace> _detections;

    public FakeFaceEngine(Dictionary<string, float[]>? embeddings = null, Dictionary<string, DetectedFace>? detections = null)
    {
        _embeddings = embeddings ?? new Dictionary<string, float[]>();
        _detections = detections ?? new Dictionary<string, DetectedFace>();
    }

    public DetectedFace? DetectLargest(byte[] jpegBytes)
    {
        try
        {
            string key = System.Text.Encoding.UTF8.GetString(jpegBytes);
            if (_detections.TryGetValue(key, out var f)) return f;
        }
        catch { }
        return null;
    }

    public float[]? Embed(byte[] jpegBytes)
    {
        try
        {
            string key = System.Text.Encoding.UTF8.GetString(jpegBytes);
            if (_embeddings.TryGetValue(key, out var e)) return e;
        }
        catch { }
        return null;
    }
}

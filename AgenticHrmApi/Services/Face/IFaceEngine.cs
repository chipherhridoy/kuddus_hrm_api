namespace AgenticHrmApi.Services.Face;

public readonly record struct DetectedFace(
    float X, float Y, float W, float H,
    float RightEyeX, float RightEyeY,
    float LeftEyeX,  float LeftEyeY,
    float NoseX,     float NoseY,
    float RightMouthX, float RightMouthY,
    float LeftMouthX,  float LeftMouthY,
    float Score);

public interface IFaceEngine
{
    /// Largest face in the image, or null. Never throws on a bad image — returns null.
    DetectedFace? DetectLargest(byte[] jpegBytes);

    /// Aligns via the 5 landmarks and returns a 128-d L2-normalised embedding, or null.
    float[]? Embed(byte[] jpegBytes);
}

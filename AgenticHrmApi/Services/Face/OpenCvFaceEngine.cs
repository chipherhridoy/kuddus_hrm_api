using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace AgenticHrmApi.Services.Face;

public class OpenCvFaceEngine : IFaceEngine, IDisposable
{
    private FaceDetectorYN? _detector;
    private readonly string _yunetPath;
    private Size _lastSize;
    
    private Net? _recognizerNet;
    private readonly object _lock = new();

    private readonly Point2f[] _dstPts = new Point2f[]
    {
        new Point2f(38.2946f, 51.6963f),
        new Point2f(73.5318f, 51.5014f),
        new Point2f(56.0252f, 71.7366f),
        new Point2f(41.5493f, 92.3655f),
        new Point2f(70.7299f, 92.2041f)
    };

    public OpenCvFaceEngine(string yunetPath, string sfacePath)
    {
        _yunetPath = yunetPath;
        _detector = null;
        if (File.Exists(sfacePath))
        {
            _recognizerNet = CvDnn.ReadNetFromOnnx(sfacePath);
        }
    }

    private void EnsureDetectorSize(Size size)
    {
        if (_detector == null || _lastSize != size)
        {
            _detector?.Dispose();
            _detector = FaceDetectorYN.Create(_yunetPath, "", size);
            _lastSize = size;
        }
    }

    public DetectedFace? DetectLargest(byte[] jpegBytes)
    {
        try
        {
            using var img = Cv2.ImDecode(jpegBytes, ImreadModes.Color);
            if (img.Empty()) return null;

            lock (_lock)
            {
                EnsureDetectorSize(new Size(img.Width, img.Height));
                using var faces = new Mat();
                _detector!.Detect(img, faces);

                int rows = faces.Rows;
                if (rows == 0) return null;

                int bestIdx = 0;
                float maxArea = 0;
                for (int i = 0; i < rows; i++)
                {
                    float w = faces.At<float>(i, 2);
                    float h = faces.At<float>(i, 3);
                    float area = w * h;
                    if (area > maxArea)
                    {
                        maxArea = area;
                        bestIdx = i;
                    }
                }

                return new DetectedFace(
                    faces.At<float>(bestIdx, 0), faces.At<float>(bestIdx, 1),
                    faces.At<float>(bestIdx, 2), faces.At<float>(bestIdx, 3),
                    faces.At<float>(bestIdx, 4), faces.At<float>(bestIdx, 5),
                    faces.At<float>(bestIdx, 6), faces.At<float>(bestIdx, 7),
                    faces.At<float>(bestIdx, 8), faces.At<float>(bestIdx, 9),
                    faces.At<float>(bestIdx, 10), faces.At<float>(bestIdx, 11),
                    faces.At<float>(bestIdx, 12), faces.At<float>(bestIdx, 13),
                    faces.At<float>(bestIdx, 14));
            }
        }
        catch
        {
            return null;
        }
    }

    public float[]? Embed(byte[] jpegBytes)
    {
        try
        {
            using var img = Cv2.ImDecode(jpegBytes, ImreadModes.Color);
            if (img.Empty()) return null;

            lock (_lock)
            {
                EnsureDetectorSize(new Size(img.Width, img.Height));
                using var faces = new Mat();
                _detector!.Detect(img, faces);

                int rows = faces.Rows;
                if (rows == 0) return null;

                int bestIdx = 0;
                float maxArea = 0;
                for (int i = 0; i < rows; i++)
                {
                    float w = faces.At<float>(i, 2);
                    float h = faces.At<float>(i, 3);
                    float area = w * h;
                    if (area > maxArea)
                    {
                        maxArea = area;
                        bestIdx = i;
                    }
                }

                var srcPts = new Point2f[5];
                srcPts[0] = new Point2f(faces.At<float>(bestIdx, 4), faces.At<float>(bestIdx, 5));
                srcPts[1] = new Point2f(faces.At<float>(bestIdx, 6), faces.At<float>(bestIdx, 7));
                srcPts[2] = new Point2f(faces.At<float>(bestIdx, 8), faces.At<float>(bestIdx, 9));
                srcPts[3] = new Point2f(faces.At<float>(bestIdx, 10), faces.At<float>(bestIdx, 11));
                srcPts[4] = new Point2f(faces.At<float>(bestIdx, 12), faces.At<float>(bestIdx, 13));

                using var srcMat = Mat.FromArray(srcPts);
                using var dstMat = Mat.FromArray(_dstPts);
                
                using var tMat = Cv2.EstimateAffinePartial2D(srcMat, dstMat);
                if (tMat == null || _recognizerNet == null)
                {
                    return null;
                }

                using var aligned = new Mat();
                Cv2.WarpAffine(img, aligned, tMat, new Size(112, 112));

                using var blob = CvDnn.BlobFromImage(aligned, 1.0, new Size(112, 112), new OpenCvSharp.Scalar(0, 0, 0), swapRB: false, crop: false);
                _recognizerNet.SetInput(blob);
                using var feature = _recognizerNet.Forward();

                float[] embedding = new float[feature.Total()];
                Marshal.Copy(feature.Data, embedding, 0, embedding.Length);

                // L2 Normalize
                float mag = 0f;
                for (int i = 0; i < embedding.Length; i++)
                    mag += embedding[i] * embedding[i];
                mag = (float)Math.Sqrt(mag);
                if (mag > 0)
                {
                    for (int i = 0; i < embedding.Length; i++)
                        embedding[i] /= mag;
                }

                return embedding;
            }
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _detector?.Dispose();
        _recognizerNet?.Dispose();
    }
}

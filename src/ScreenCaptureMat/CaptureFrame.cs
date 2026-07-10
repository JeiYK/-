using OpenCvSharp;

namespace ScreenCaptureMat;

public sealed record CaptureFrame(Mat Mat, TimeSpan CaptureElapsed, DateTimeOffset Timestamp) : IDisposable
{
    public void Dispose() => Mat.Dispose();
}

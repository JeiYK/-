# ScreenCaptureMat

A .NET 8 Windows helper for high-frequency screen-region capture into OpenCvSharp `Mat` objects.

## Design

- Uses `GetDC(NULL)` + memory DC + a reusable top-down 32-bit DIB section.
- Captures with `BitBlt` into the existing DIB buffer, avoiding per-frame bitmap allocation.
- Exposes the buffer as `MatType.CV_8UC4` (Windows BGRA order).
- Provides a `PeriodicScreenCaptureTask` wrapper for a non-overlapping 10 ms Task loop.

> A sub-10 ms capture budget depends on hardware, capture size, DWM/GPU load, DPI mode, and whether layered windows are included. For the best latency, reuse one `FastScreenMatCapturer`, keep the region small, and use `CaptureUnsafeView` or `CaptureInto` instead of cloning each frame.

## Basic usage

```csharp
using ScreenCaptureMat;

var hwnd = /* handle of the host/carrier window */;
var region = FastScreenMatCapturer.WindowClientRegionToScreen(hwnd, 100, 100, 320, 240);

using var capturer = new FastScreenMatCapturer(region.Width, region.Height);
var mat = capturer.CaptureUnsafeView(region.X, region.Y);
```

## 10 ms Task loop

```csharp
await using var loop = new PeriodicScreenCaptureTask(
    capturer,
    region,
    (frame, ct) =>
    {
        // frame.Mat is CV_8UC4 BGRA.
        // If cloneFrame is false, consume it before the next tick.
        return ValueTask.CompletedTask;
    },
    TimeSpan.FromMilliseconds(10),
    cloneFrame: false);

await loop.StartAsync();
```

Run the sample on Windows:

```powershell
dotnet run --project samples/ScreenCaptureMat.Sample -- 100 100 320 240
```

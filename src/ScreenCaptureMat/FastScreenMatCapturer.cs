using System.Runtime.InteropServices;
using OpenCvSharp;

namespace ScreenCaptureMat;

/// <summary>
/// Captures screen regions into reusable BGRA buffers and exposes the pixels as OpenCV Mat instances.
/// </summary>
/// <remarks>
/// This class is optimized for high-frequency capture loops. Create one instance per capture size,
/// reuse it, and avoid calling <see cref="CaptureClone"/> if the caller can consume the unsafe view
/// before the next capture overwrites the internal buffer.
/// </remarks>
public sealed class FastScreenMatCapturer : IDisposable
{
    private readonly object syncRoot = new();
    private readonly int width;
    private readonly int height;
    private readonly int stride;
    private readonly nint screenDc;
    private readonly nint memoryDc;
    private readonly nint bitmap;
    private readonly nint previousBitmap;
    private readonly nint bits;
    private readonly Mat reusableView;
    private bool disposed;

    public FastScreenMatCapturer(int width, int height)
    {
        if (OperatingSystem.IsWindows() is false)
        {
            throw new PlatformNotSupportedException("FastScreenMatCapturer requires Windows GDI APIs.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
        }

        this.width = width;
        this.height = height;
        stride = width * 4;

        _ = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);

        screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new InvalidOperationException("GetDC(NULL) failed.");
        }

        memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        if (memoryDc == nint.Zero)
        {
            NativeMethods.ReleaseDC(nint.Zero, screenDc);
            throw new InvalidOperationException("CreateCompatibleDC failed.");
        }

        var bitmapInfo = new NativeMethods.BitmapInfo
        {
            bmiHeader = new NativeMethods.BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BiRgb,
                biSizeImage = (uint)(stride * height)
            }
        };

        bitmap = NativeMethods.CreateDIBSection(
            screenDc,
            in bitmapInfo,
            NativeMethods.DibRgbColors,
            out bits,
            nint.Zero,
            0);

        if (bitmap == nint.Zero || bits == nint.Zero)
        {
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(nint.Zero, screenDc);
            throw new InvalidOperationException("CreateDIBSection failed.");
        }

        previousBitmap = NativeMethods.SelectObject(memoryDc, bitmap);
        reusableView = Mat.FromPixelData(height, width, MatType.CV_8UC4, bits, stride);
    }

    public int Width => width;

    public int Height => height;

    /// <summary>
    /// Captures a physical screen-pixel region and returns a Mat view over the internal reusable buffer.
    /// The returned Mat is overwritten by the next capture and must not be disposed by the caller.
    /// </summary>
    public Mat CaptureUnsafeView(int screenX, int screenY, bool includeLayeredWindows = true)
    {
        ThrowIfDisposed();

        lock (syncRoot)
        {
            var rasterOperation = includeLayeredWindows
                ? NativeMethods.Srccopy | NativeMethods.Captureblt
                : NativeMethods.Srccopy;

            if (NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, screenX, screenY, rasterOperation) is false)
            {
                throw new InvalidOperationException("BitBlt failed.");
            }

            return reusableView;
        }
    }

    /// <summary>
    /// Captures a physical screen-pixel region and returns an independent Mat clone.
    /// This is safer across async boundaries, but it adds one memory copy per frame.
    /// </summary>
    public Mat CaptureClone(int screenX, int screenY, bool includeLayeredWindows = true)
    {
        lock (syncRoot)
        {
            return CaptureUnsafeView(screenX, screenY, includeLayeredWindows).Clone();
        }
    }

    /// <summary>
    /// Copies a captured physical screen-pixel region into a caller-owned Mat.
    /// The destination Mat must be CV_8UC4 and match this capturer's width and height.
    /// </summary>
    public void CaptureInto(Mat destination, int screenX, int screenY, bool includeLayeredWindows = true)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Width != width || destination.Height != height || destination.Type() != MatType.CV_8UC4)
        {
            throw new ArgumentException("Destination Mat must match the capture size and use CV_8UC4.", nameof(destination));
        }

        lock (syncRoot)
        {
            CaptureUnsafeView(screenX, screenY, includeLayeredWindows).CopyTo(destination);
        }
    }

    public static CaptureRegion WindowClientRegionToScreen(nint hwnd, int clientX, int clientY, int width, int height)
    {
        var point = new NativeMethods.Point { X = clientX, Y = clientY };
        if (NativeMethods.ClientToScreen(hwnd, ref point) is false)
        {
            throw new InvalidOperationException("ClientToScreen failed. Check that the window handle is valid.");
        }

        return new CaptureRegion(point.X, point.Y, width, height);
    }

    public static CaptureRegion WindowRegionToScreen(nint hwnd, int windowX, int windowY, int width, int height)
    {
        if (NativeMethods.GetWindowRect(hwnd, out var rect) is false)
        {
            throw new InvalidOperationException("GetWindowRect failed. Check that the window handle is valid.");
        }

        return new CaptureRegion(rect.Left + windowX, rect.Top + windowY, width, height);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        reusableView.Dispose();

        if (previousBitmap != nint.Zero)
        {
            NativeMethods.SelectObject(memoryDc, previousBitmap);
        }

        if (bitmap != nint.Zero)
        {
            NativeMethods.DeleteObject(bitmap);
        }

        if (memoryDc != nint.Zero)
        {
            NativeMethods.DeleteDC(memoryDc);
        }

        if (screenDc != nint.Zero)
        {
            NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }

        disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

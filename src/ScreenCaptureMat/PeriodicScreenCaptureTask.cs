using System.Diagnostics;
using OpenCvSharp;

namespace ScreenCaptureMat;

/// <summary>
/// Runs a non-overlapping high-frequency screen capture loop on a background Task.
/// </summary>
public sealed class PeriodicScreenCaptureTask : IAsyncDisposable
{
    private readonly FastScreenMatCapturer capturer;
    private readonly CaptureRegion region;
    private readonly Func<CaptureFrame, CancellationToken, ValueTask> onFrameAsync;
    private readonly TimeSpan period;
    private readonly bool cloneFrame;
    private readonly CancellationTokenSource stopSource = new();
    private CancellationTokenSource? linkedStopSource;
    private Task? runningTask;
    private bool disposed;

    public PeriodicScreenCaptureTask(
        FastScreenMatCapturer capturer,
        CaptureRegion region,
        Func<CaptureFrame, CancellationToken, ValueTask> onFrameAsync,
        TimeSpan? period = null,
        bool cloneFrame = true)
    {
        ArgumentNullException.ThrowIfNull(capturer);
        ArgumentNullException.ThrowIfNull(onFrameAsync);
        region.ThrowIfInvalid();

        if (capturer.Width != region.Width || capturer.Height != region.Height)
        {
            throw new ArgumentException("Region size must match the capturer size.", nameof(region));
        }

        this.capturer = capturer;
        this.region = region;
        this.onFrameAsync = onFrameAsync;
        this.period = period ?? TimeSpan.FromMilliseconds(10);
        this.cloneFrame = cloneFrame;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (runningTask is not null)
        {
            throw new InvalidOperationException("The capture task has already been started.");
        }

        linkedStopSource = CancellationTokenSource.CreateLinkedTokenSource(stopSource.Token, cancellationToken);
        runningTask = Task.Run(() => CaptureLoopAsync(linkedStopSource.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (runningTask is null)
        {
            return;
        }

        await stopSource.CancelAsync().ConfigureAwait(false);

        try
        {
            await runningTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopSource.IsCancellationRequested)
        {
        }

        linkedStopSource?.Dispose();
        linkedStopSource = null;
        runningTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        stopSource.Dispose();
        disposed = true;
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(period);
        var stopwatch = new Stopwatch();

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            stopwatch.Restart();
            Mat mat = cloneFrame
                ? capturer.CaptureClone(region.X, region.Y)
                : capturer.CaptureUnsafeView(region.X, region.Y);
            stopwatch.Stop();

            var frame = new CaptureFrame(mat, stopwatch.Elapsed, DateTimeOffset.UtcNow);
            try
            {
                await onFrameAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (cloneFrame)
                {
                    frame.Dispose();
                }
            }
        }
    }
}

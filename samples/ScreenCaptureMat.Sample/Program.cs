using OpenCvSharp;
using ScreenCaptureMat;

if (args.Length < 4)
{
    Console.WriteLine("Usage: ScreenCaptureMat.Sample <screenX> <screenY> <width> <height> [windowHandleHex]");
    Console.WriteLine("Example: ScreenCaptureMat.Sample 100 100 320 240");
    return;
}

var x = int.Parse(args[0]);
var y = int.Parse(args[1]);
var width = int.Parse(args[2]);
var height = int.Parse(args[3]);

var region = args.Length >= 5
    ? FastScreenMatCapturer.WindowClientRegionToScreen((nint)Convert.ToInt64(args[4], 16), x, y, width, height)
    : new CaptureRegion(x, y, width, height);

using var capturer = new FastScreenMatCapturer(region.Width, region.Height);

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var frameCount = 0;
var slowFrames = 0;
var totalMs = 0.0;

await using var task = new PeriodicScreenCaptureTask(
    capturer,
    region,
    (frame, _) =>
    {
        frameCount++;
        totalMs += frame.CaptureElapsed.TotalMilliseconds;
        if (frame.CaptureElapsed > TimeSpan.FromMilliseconds(10))
        {
            slowFrames++;
        }

        if (frameCount % 100 == 0)
        {
            Console.WriteLine($"frames={frameCount}, last={frame.CaptureElapsed.TotalMilliseconds:F3} ms");
        }

        return ValueTask.CompletedTask;
    },
    TimeSpan.FromMilliseconds(10),
    cloneFrame: false);

await task.StartAsync(cts.Token);

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
}
catch (OperationCanceledException)
{
}

await task.StopAsync();
Console.WriteLine($"Captured {frameCount} frames. Average capture time: {totalMs / Math.Max(1, frameCount):F3} ms. >10ms frames: {slowFrames}.");

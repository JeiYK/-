namespace ScreenCaptureMat;

/// <summary>
/// A rectangle in physical screen pixels.
/// </summary>
public readonly record struct CaptureRegion(int X, int Y, int Width, int Height)
{
    public void ThrowIfInvalid()
    {
        if (Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), Width, "Width must be greater than zero.");
        }

        if (Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Height), Height, "Height must be greater than zero.");
        }
    }
}

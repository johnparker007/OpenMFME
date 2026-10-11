using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OpenMFME.Desktop;

public sealed class LayoutReelControl : Control
{
    private readonly Bitmap band;
    private readonly Bitmap? overlay;
    private readonly Bitmap? gradient;
    private readonly bool horizontal;
    private readonly bool reversed;
    private readonly double bandOffset;
    private readonly double offBrightness;
    private readonly bool hasLamps;
    private double position;
    private bool lampsOn;
    public int ComponentIndex { get; }
    public int ReelNumber { get; }
    public int Stops { get; }
    public double Position => position;
    public bool LampsOn => lampsOn;

    internal LayoutReelControl(int index, int number, int stops, double bandOffset, bool horizontal,
        bool reversed, bool hasLamps, double offBrightness, Bitmap band, Bitmap? overlay, Bitmap? gradient)
    {
        ComponentIndex = index; ReelNumber = number; Stops = Math.Clamp(stops, 1, 256);
        this.bandOffset = bandOffset; this.horizontal = horizontal; this.reversed = reversed;
        this.hasLamps = hasLamps; this.offBrightness = Math.Clamp(offBrightness, 0, 1);
        this.band = band; this.overlay = overlay; this.gradient = gradient;
        ClipToBounds = true;
    }

    // Position is expressed in symbol stops, independent of display refresh rate or motor steps.
    public void SetState(double symbolPosition, bool lit)
    {
        if (!double.IsFinite(symbolPosition)) throw new ArgumentOutOfRangeException(nameof(symbolPosition));
        position = Wrap(symbolPosition, Stops); lampsOn = lit; InvalidateVisual();
    }

    private static double Wrap(double value, double length) => ((value % length) + length) % length;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        var length = horizontal ? band.PixelSize.Width : band.PixelSize.Height;
        var cross = horizontal ? band.PixelSize.Height : band.PixelSize.Width;
        var pitch = (double)length / Stops;
        var sourceSpan = Math.Min(3, Stops) * pitch;
        var centre = (bandOffset + (reversed ? -position : position) + 0.5) * pitch;
        var source = Wrap(centre - sourceSpan / 2, length);
        var consumed = 0.0;
        using (context.PushClip(new Rect(size)))
        {
            // Split at the end of the strip so the last-to-first symbol transition is seamless.
            while (consumed < sourceSpan - 0.00001)
            {
                var take = Math.Min(length - source, sourceSpan - consumed);
                var sourceRect = horizontal ? new Rect(source, 0, take, cross) : new Rect(0, source, cross, take);
                var targetRect = horizontal
                    ? new Rect(consumed / sourceSpan * size.Width, 0, take / sourceSpan * size.Width, size.Height)
                    : new Rect(0, consumed / sourceSpan * size.Height, size.Width, take / sourceSpan * size.Height);
                context.DrawImage(band, sourceRect, targetRect);
                consumed += take; source = 0;
            }
            if (hasLamps && !lampsOn)
                context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)((1 - offBrightness) * 255), 0, 0, 0)), new Rect(size));
            if (gradient is not null) context.DrawImage(gradient, new Rect(size));
            if (overlay is not null) context.DrawImage(overlay, new Rect(size));
        }
    }
}

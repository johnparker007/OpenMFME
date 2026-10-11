using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OpenMFME.Desktop;

// Bitmap ownership belongs to LayoutSurface; switching state never decodes or allocates images.
public sealed class LayoutLampControl : Control
{
    private readonly Bitmap? off;
    private readonly Bitmap? on;
    private bool isOn;
    public int ComponentIndex { get; }
    public IReadOnlyList<int> LampNumbers { get; }
    public bool IsOn => isOn;

    internal LayoutLampControl(int index, IReadOnlyList<int> numbers, Bitmap? off, Bitmap? on)
    { ComponentIndex = index; LampNumbers = numbers; this.off = off; this.on = on; }

    public void SetState(bool lit)
    {
        if (isOn == lit) return;
        isOn = lit; InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if ((isOn ? on ?? off : off) is { } image)
            context.DrawImage(image, new Rect(Bounds.Size));
    }
}

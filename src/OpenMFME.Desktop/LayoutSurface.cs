using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenMFME.LayoutDecoder;

namespace OpenMFME.Desktop;

// Owns rendered bitmap resources independently of any window or inspector.
internal sealed class LayoutSurface : IDisposable
{
    private readonly List<Bitmap> bitmaps = [];
    private readonly Dictionary<int, Border> outlines = [];
    public Canvas Canvas { get; }
    public int ImageFailures { get; }
    public Action<LayoutComponent>? Selected { get; set; }

    public LayoutSurface(LayoutDocument document)
    {
        Canvas = new Canvas { Name = "LayoutCanvas", Width = document.Width, Height = document.Height, ClipToBounds = true };
        var background = document.Components.FirstOrDefault(component => component.Type == "Background");
        Canvas.Background = ReadColour(background?.Properties["Values"]?["Colour"]?.GetValue<string>(), Brushes.LightGray);
        var imageFailures = 0;
        foreach (var component in document.Components)
        {
            if (component.Width == 0 || component.Height == 0) continue;
            Control? visual = null;
            if (component.Type is "Background" or "Bitmap")
            {
                var image = component.Images.FirstOrDefault(item => item.Key == "background_image") ?? component.Images.FirstOrDefault();
                if (image is not null)
                {
                    try
                    {
                        using var imageStream = new MemoryStream(image.Bytes, writable: false);
                        var bitmap = new Bitmap(imageStream);
                        bitmaps.Add(bitmap); visual = new Image { Source = bitmap, Stretch = Stretch.Fill };
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
                    { imageFailures++; }
                }
            }
            else if (component.Type == "Label")
            {
                var values = component.Properties["Values"];
                var text = values?["Label (UTF-16)"]?.GetValue<string>() ?? values?["Caption"]?.GetValue<string>() ?? values?["Label"]?.GetValue<string>() ?? "";
                var font = component.Properties["Fonts"]?["PrimaryFont"];
                visual = new TextBlock { Name = "LayoutLabel" + component.Index, Text = text,
                    FontFamily = new FontFamily(font?["FontName"]?.GetValue<string>() ?? "Tahoma"),
                    FontSize = Math.Clamp((font?["FontSize"]?.GetValue<double>() ?? 8) * 4 / 3, 6, 144),
                    Foreground = ReadColour(font?["TextColour"]?.GetValue<string>(), Brushes.Black),
                    Background = ReadColour(values?["BackgroundColour"]?.GetValue<string>(), Brushes.Transparent),
                    TextWrapping = TextWrapping.Wrap };
            }
            if (visual is not null) Place(visual, component);
            if (component.Type == "Background") continue;
            var outline = new Border { Name = "ComponentOutline" + component.Index, BorderBrush = Brushes.DeepSkyBlue, BorderThickness = new Thickness(1), Background = Brushes.Transparent };
            ToolTip.SetTip(outline, $"#{component.Index} {component.Type} · number {component.Number}");
            outline.PointerPressed += (_, _) => Selected?.Invoke(component);
            outlines[component.Index] = outline; Place(outline, component);
        }
        ImageFailures = imageFailures;
        SetOutlines(false);
    }

    private void Place(Control control, LayoutComponent component)
    {
        control.Width = component.Width; control.Height = component.Height;
        Canvas.SetLeft(control, component.X); Canvas.SetTop(control, component.Y);
        if (component.Angle != 0) control.RenderTransform = new RotateTransform(component.Angle);
        Canvas.Children.Add(control);
    }

    public void SetOutlines(bool visible, int? selected = null)
    {
        foreach (var (index, border) in outlines)
        {
            border.IsVisible = visible;
            border.BorderBrush = index == selected ? Brushes.Yellow : Brushes.DeepSkyBlue;
            border.BorderThickness = new Thickness(index == selected ? 2 : 1);
        }
    }

    public void Dispose()
    {
        Canvas.Children.Clear();
        foreach (var bitmap in bitmaps) bitmap.Dispose();
        bitmaps.Clear();
    }

    private static IBrush ReadColour(string? rgba, IBrush fallback)
    {
        if (rgba is null || rgba.Length != 9) return fallback;
        return Color.TryParse("#" + rgba[7..9] + rgba[1..7], out var color) ? new SolidColorBrush(color) : fallback;
    }
}

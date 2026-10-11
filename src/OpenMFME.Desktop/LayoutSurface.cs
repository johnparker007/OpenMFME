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
    private readonly List<LayoutReelControl> reels = [];
    private readonly List<LayoutLampControl> lamps = [];
    public bool HasAnimatedControls => reels.Count > 0 || lamps.Count > 0;
    public Canvas Canvas { get; }
    public int ImageFailures { get; private set; }
    public Action<LayoutComponent>? Selected { get; set; }

    public LayoutSurface(LayoutDocument document)
    {
        Canvas = new Canvas { Name = "LayoutCanvas", Width = document.Width, Height = document.Height, ClipToBounds = true };
        var background = document.Components.FirstOrDefault(component => component.Type == "Background");
        Canvas.Background = ReadColour(background?.Properties["Values"]?["Colour"]?.GetValue<string>(), Brushes.LightGray);

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
                    { ImageFailures++; }
                }
            }
            else if (component.Type == "Reel")
            {
                var band = LoadImage(component, "band_image");
                if (band is not null)
                {
                    var values = component.Properties["Values"];
                    var reel = new LayoutReelControl(component.Index, component.Number,
                        values?["Stops"]?.GetValue<int>() ?? 16, values?["BandOffset"]?.GetValue<double>() ?? 0,
                        component.Properties["Geometry"]?["Orientation"]?.GetValue<string>() == "Horizontal",
                        values?["Reversed"]?.GetValue<bool>() == true, values?["LampsEnabled"]?.GetValue<bool>() == true,
                        (values?["OffLevel"]?.GetValue<double>() ?? 64) / 255,
                        band, LoadImage(component, "overlay_image", greenKey: true), LoadImage(component, "gradient_image", shading: true))
                        { Name = "LayoutReel" + component.Index };
                    reels.Add(reel); visual = reel;
                }
            }
            else if (component.Type == "Lamp")
            {
                var transparent = component.Properties["Values"]?["Transparent"]?.GetValue<bool>() == true;
                var off = LoadImage(component, "brightmask_main", transparent);
                var on = LoadImage(component, "sublamp_1_main_image", transparent);
                if (off is not null || on is not null)
                {
                    var numbers = (component.Properties["SubLampNumberTable"] as System.Text.Json.Nodes.JsonObject)?
                        .Where(pair => pair.Key.StartsWith("Lamp", StringComparison.Ordinal)).Select(pair => pair.Value!.GetValue<int>()).Where(value => value >= 0).Distinct().ToArray() ?? [];
                    var lamp = new LayoutLampControl(component.Index, numbers, off, on) { Name = "LayoutLamp" + component.Index };
                    lamps.Add(lamp); visual = lamp;
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
        SetOutlines(false);
    }

    private Bitmap? LoadImage(LayoutComponent component, string key, bool greenKey = false, bool shading = false)
    {
        var image = component.Images.FirstOrDefault(item => item.Key == key);
        if (image is null) return null;
        try
        {
            var bitmap = LayoutArtwork.Load(image, greenKey, shading);
            bitmaps.Add(bitmap); return bitmap;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        { ImageFailures++; return null; }
    }

    public void ApplyRenderTest(double seconds)
    {
        var lit = ((long)(seconds * 2) & 1) == 0;
        foreach (var reel in reels) reel.SetState(seconds * (5 + Math.Abs(reel.ReelNumber % 3)), lit);
        foreach (var lamp in lamps) lamp.SetState(lit);
    }

    public void ResetOutputs()
    {
        foreach (var reel in reels) reel.SetState(0, false);
        foreach (var lamp in lamps) lamp.SetState(false);
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
        Canvas.Children.Clear(); reels.Clear(); lamps.Clear();
        foreach (var bitmap in bitmaps) bitmap.Dispose();
        bitmaps.Clear();
    }

    private static IBrush ReadColour(string? rgba, IBrush fallback)
    {
        if (rgba is null || rgba.Length != 9) return fallback;
        return Color.TryParse("#" + rgba[7..9] + rgba[1..7], out var color) ? new SolidColorBrush(color) : fallback;
    }
}

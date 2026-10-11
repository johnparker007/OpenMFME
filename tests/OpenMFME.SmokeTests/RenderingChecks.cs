using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenMFME.Desktop;
using OpenMFME.LayoutDecoder;

internal static class RenderingChecks
{
    private static LayoutImage Image(string key, int width, int height, Func<int, int, Color> colour)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var frame = bitmap.Lock())
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var c = colour(x, y); var offset = y * frame.RowBytes + x * 4;
                Marshal.WriteByte(frame.Address, offset, c.B); Marshal.WriteByte(frame.Address, offset + 1, c.G);
                Marshal.WriteByte(frame.Address, offset + 2, c.R); Marshal.WriteByte(frame.Address, offset + 3, c.A);
            }
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions());
        return new LayoutImage(key, width, height, stream.ToArray());
    }

    private static Color Pixel(Bitmap bitmap, int x, int y)
    {
        using var copy = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var frame = copy.Lock(); bitmap.CopyPixels(frame); var offset = y * frame.RowBytes + x * 4;
        return Color.FromArgb(Marshal.ReadByte(frame.Address, offset + 3), Marshal.ReadByte(frame.Address, offset + 2),
            Marshal.ReadByte(frame.Address, offset + 1), Marshal.ReadByte(frame.Address, offset));
    }

    private static Color Sample(Window window, int x, int y)
    {
        Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame()!;
        return Pixel(frame, x, y);
    }

    public static void Run(Action<bool, string> check)
    {
        var keyed = Image("key", 3, 1, (x, _) => x == 0 ? Colors.Lime : x == 1 ? Colors.Red : Color.FromArgb(128, 0, 0, 255));
        using (var image = LayoutArtwork.Load(keyed, greenKey: true))
        {
            check(Pixel(image, 0, 0).A == 0 && Pixel(image, 1, 0) == Colors.Red, "Green key removed artwork or retained its transparent background");
            check(Math.Abs(Pixel(image, 2, 0).A - 128) <= 1, "Colour-key conversion lost existing alpha");
        }
        using (var opaque = LayoutArtwork.Load(keyed)) check(Pixel(opaque, 0, 0) == Colors.Lime, "Opaque image unexpectedly colour-keyed");
        using (var gradient = LayoutArtwork.Load(Image("gradient", 2, 1, (x, _) => x == 0 ? Colors.Black : Colors.White), shading: true))
            check(Pixel(gradient, 0, 0).A == 255 && Pixel(gradient, 1, 0).A == 0, "Reel gradient transmission is inverted");

        Color[] colours = [Colors.Red, Colors.Blue, Colors.Yellow, Colors.Cyan, Colors.Magenta, Colors.White];
        var band = Image("band_image", 6, 60, (_, y) => colours[y / 10]);
        var off = Image("brightmask_main", 4, 4, (x, y) => x == 0 || y == 0 ? Colors.Lime : Colors.Red);
        var on = Image("sublamp_1_main_image", 4, 4, (x, y) => x == 0 || y == 0 ? Colors.Lime : Colors.Yellow);
        var reelProperties = JsonNode.Parse("""{"Values":{"Stops":6,"BandOffset":0},"Geometry":{"Orientation":"Vertical"}}""")!.AsObject();
        var lampProperties = JsonNode.Parse("""{"Values":{"Transparent":true},"SubLampNumberTable":{"Lamp1":7,"Lamp2":9}}""")!.AsObject();
        LayoutComponent[] components = [
            new(0, "Reel", 0, 0, 6, 30, 2, 0, reelProperties, [band]),
            new(1, "Lamp", 10, 0, 4, 4, 7, 0, lampProperties, [off, on])];
        using var surface = new LayoutSurface(new LayoutDocument("synthetic", "", 20, 30, components));
        var window = new Window { Width = 20, Height = 30, Content = surface.Canvas }; window.Show();
        var reel = surface.Canvas.Children.OfType<LayoutReelControl>().Single();
        var lamp = surface.Canvas.Children.OfType<LayoutLampControl>().Single();
        check(surface.HasAnimatedControls && lamp.LampNumbers.SequenceEqual(new[] { 7, 9 }), "Surface lost lamp mappings or animated controls");
        check(Sample(window, 2, 15) == Colors.Red && Sample(window, 12, 2) == Colors.Red, "Idle reel/lamps do not use saved artwork");
        check(Sample(window, 2, 5) == Colors.White && Sample(window, 2, 25) == Colors.Blue, "Reel band boundary left an empty or repeated seam");
        check(Sample(window, 10, 0) == Colors.LightGray, "Lamp transparent pixels cover the cabinet");
        reel.SetState(1, true); check(Sample(window, 2, 15) == Colors.Blue, "Reel did not advance by a symbol stop");
        reel.SetState(6, true); check(Sample(window, 2, 15) == Colors.Red, "Reel wrap did not return to the first symbol");
        reel.SetState(-1, true); check(Sample(window, 2, 15) == Colors.White, "Negative reel position did not wrap");
        reel.SetState(0.5, true); var between = Sample(window, 2, 15);
        check(between != Colors.Red && between.A == 255, "Fractional reel position left a seam");
        try { reel.SetState(double.NaN, false); throw new Exception("Non-finite reel position accepted"); }
        catch (ArgumentOutOfRangeException) { check(true, "Invalid reel position rejected"); }
        surface.ApplyRenderTest(0.2);
        check(lamp.IsOn && reel.LampsOn && reel.Position > 0 && Sample(window, 12, 2) == Colors.Yellow, "Render test did not light and move saved artwork");
        surface.ApplyRenderTest(0.7); check(!lamp.IsOn && !reel.LampsOn && Sample(window, 12, 2) == Colors.Red, "Render test did not flash lamps off");
        surface.ResetOutputs(); check(!lamp.IsOn && reel.Position == 0 && Sample(window, 2, 15) == Colors.Red, "Stopping render test did not restore idle outputs");
        window.Content = null; surface.Dispose(); check(surface.Canvas.Children.Count == 0 && !surface.HasAnimatedControls, "Disposed surface retained render controls"); window.Close();

        // Horizontal bands use the same wrap rule along the other axis.
        using var horizontalBand = LayoutArtwork.Load(Image("horizontal", 60, 6, (x, _) => colours[x / 10]));
        var horizontal = new LayoutReelControl(3, 0, 6, 1, true, true, false, 1, horizontalBand, null, null) { Width = 30, Height = 6 };
        var horizontalWindow = new Window { Width = 30, Height = 6, Content = horizontal }; horizontalWindow.Show();
        check(Sample(horizontalWindow, 15, 2) == Colors.Blue, "Horizontal reel ignored band offset");
        horizontal.SetState(1, false); check(Sample(horizontalWindow, 15, 2) == Colors.Red, "Horizontal reel ignored reversed direction");
        horizontalWindow.Close();
    }
}

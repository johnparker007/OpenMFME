using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenMFME.Formats;

namespace OpenMFME.Desktop.Reconstruction;

/// <summary>Shared glyph cache lives for the app lifetime; previews do not own these bitmaps.</summary>
public static class RecoveredGraphics
{
    private static readonly Dictionary<string, WriteableBitmap> Cache = new(StringComparer.Ordinal);
    public static IImage? Glyph(DfmComponent node, bool disabled)
    {
        if (!node.Properties.TryGetValue("Glyph.Data", out var value)) return null;
        var hash = value.Value?["sha256"]?.GetValue<string>();
        if (hash is null) return null;
        var frames = Math.Clamp((int)node.Number("NumGlyphs", 1), 1, 4);
        // Delphi's second glyph represents disabled state when present.
        var frame = disabled && frames > 1 ? 1 : 0;
        var key = $"{hash}:{frames}:{frame}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var uri = new Uri($"avares://OpenMFME.Desktop/Assets/recovered-images/{hash}.bmp");
        if (!AssetLoader.Exists(uri)) return null;
        using var stream = AssetLoader.Open(uri);
        using var bitmap = new Bitmap(stream);
        if (bitmap.PixelSize.Width % frames != 0) throw new InvalidDataException("Invalid glyph strip");
        using var decoded = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var width = bitmap.PixelSize.Width / frames;
        var height = bitmap.PixelSize.Height;
        var pixels = new byte[width * height * 4];
        using (var source = decoded.Lock())
        {
            bitmap.CopyPixels(source);
            for (var y = 0; y < height; y++)
                Marshal.Copy(IntPtr.Add(source.Address, y * source.RowBytes + frame * width * 4), pixels, y * width * 4, width * 4);
        }
        // VCL automatic transparency uses the bottom-left pixel of the glyph.
        var corner = (height - 1) * width * 4;
        var blue = pixels[corner]; var green = pixels[corner + 1]; var red = pixels[corner + 2];
        for (var i = 0; i < pixels.Length; i += 4)
            pixels[i + 3] = pixels[i] == blue && pixels[i + 1] == green && pixels[i + 2] == red ? (byte)0 : (byte)255;
        var result = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var destination = result.Lock())
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width * 4, IntPtr.Add(destination.Address, y * destination.RowBytes), width * 4);
        Cache.Add(key, result);
        return result;
    }

    public static Control EmptyCanvas(DfmComponent node)
    {
        var surface = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Silver, BorderThickness = new Thickness(1)
        };
        ToolTip.SetTip(surface, "No artwork saved in this form; the original program supplies it at runtime.");
        return surface;
    }

    public static Control ColourSwatch(DfmComponent node, IBrush colour, IBrush outline) => new Border
    {
        Background = colour, BorderBrush = outline,
        BorderThickness = new Thickness(node.Number("OutlineWidth", 1)),
        IsHitTestVisible = false
    };

    public static Control DipSwitch(DfmComponent node)
    {
        var track = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#343A40")),
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Padding = new Thickness(4),
            Child = new Border { Background = Brushes.WhiteSmoke, Height = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom }
        };
        ToolTip.SetTip(track, "DIP switch preview · Appearance is inferred; no machine input is connected.");
        return track;
    }
}

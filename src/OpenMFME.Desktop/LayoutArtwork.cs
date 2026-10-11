using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenMFME.LayoutDecoder;

namespace OpenMFME.Desktop;

internal static class LayoutArtwork
{
    // MFME graphic lamps and reel overlays in the reference layout key out pure green.
    public static Bitmap Load(LayoutImage image, bool greenKey = false, bool shading = false)
    {
        using var stream = new MemoryStream(image.Bytes, writable: false);
        var original = new Bitmap(stream);
        if (!greenKey && !shading) return original;
        using (original)
        {
            var result = new WriteableBitmap(original.PixelSize, original.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            try
            {
                using var frame = result.Lock();
                original.CopyPixels(frame);
                var pixels = new byte[checked(frame.RowBytes * frame.Size.Height)];
                Marshal.Copy(frame.Address, pixels, 0, pixels.Length);
                for (var y = 0; y < frame.Size.Height; y++)
                for (var x = 0; x < frame.Size.Width; x++)
                {
                    var offset = y * frame.RowBytes + x * 4;
                    var b = pixels[offset]; var g = pixels[offset + 1]; var red = pixels[offset + 2];
                    if (greenKey && b == 0 && g == 255 && red == 0)
                    { pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = pixels[offset + 3] = 0; }
                    else if (shading)
                    {
                        // Interpret a saved grayscale reel gradient as light transmission.
                        pixels[offset + 3] = (byte)((255 - (red + g + b) / 3) * pixels[offset + 3] / 255);
                        pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 0;
                    }
                }
                Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
    }
}

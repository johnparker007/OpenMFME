using System.Buffers.Binary;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.src.Decoder.Component.Core;
using MfmeFmlDecoder.Utilities;

namespace OpenMFME.LayoutDecoder;

public sealed record LayoutImage(string Key, int Width, int Height, byte[] Bytes);
public sealed record LayoutComponent(int Index, string Type, double X, double Y, double Width, double Height, int Number,
    float Angle, JsonObject Properties, IReadOnlyList<LayoutImage> Images);
public sealed record LayoutDocument(string FilePath, string Description, double Width, double Height, IReadOnlyList<LayoutComponent> Components);

public static class FmlLayoutReader
{
    public const int MaximumFileBytes = 128 * 1024 * 1024;
    public const int MaximumDecodedBytes = 256 * 1024 * 1024;
    private static readonly object DecodeLock = new();

    public static LayoutDocument Read(string path, CancellationToken token = default)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("The layout exceeds the 128 MiB input limit.");
        using var buffer = new MemoryStream();
        var block = new byte[65536];
        int count;
        while ((count = stream.Read(block)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (buffer.Length + count > MaximumFileBytes) throw new InvalidDataException("The layout exceeds the input limit.");
            buffer.Write(block, 0, count);
        }
        return Decode(buffer.ToArray(), Path.GetFullPath(path), token);
    }

    public static LayoutDocument Decode(byte[] bytes, string sourcePath, CancellationToken token = default)
    {
        ValidateEnvelope(bytes);
        lock (DecodeLock)
        {
            token.ThrowIfCancellationRequested();
            var previousQuiet = RunLog.Quiet; RunLog.Quiet = true;
            try
            {
                var plain = FmlDecryptor.Decrypt(bytes);
                token.ThrowIfCancellationRequested();
                var version = MfmeVersionReader.Read(plain, 0);
                if (!version.Contains("20.1", StringComparison.Ordinal))
                    throw new InvalidDataException($"This layout was saved by {version}. Please resave a copy in MFME v20.1.");
                var parser = new ComponentParser();
                var walker = new FileWalker(new ComponentWalker(parser));
                walker.WalkTlv(plain, 0);
                token.ThrowIfCancellationRequested();
                var layout = parser.ToLayout(walker.Header);
                if (layout.Components.Count == 0 || layout.Components.Count > 10000)
                    throw new InvalidDataException("The layout has no components or exceeds the component limit.");
                var components = layout.Components.Select((component, index) =>
                {
                    if (component.X > 32768 || component.Y > 32768 || component.Width > 16384 || component.Height > 16384)
                        throw new InvalidDataException("A component exceeds the supported coordinate range.");
                    var images = component.Images.Select(pair =>
                    {
                        if (pair.Value.Width <= 0 || pair.Value.Height <= 0 || (long)pair.Value.Width * pair.Value.Height > 32 * 1024 * 1024)
                            throw new InvalidDataException("A layout image exceeds the supported dimensions.");
                        return new LayoutImage(pair.Key, pair.Value.Width, pair.Value.Height, pair.Value.Bytes);
                    }).ToArray();
                    return new LayoutComponent(index, component.GetType().Name, component.X, component.Y, component.Width,
                        component.Height, component.Number, component.Angle, component.ToJsonObject(), images);
                }).ToArray();
                var background = components.FirstOrDefault(component => component.Type == "Background");
                var width = background?.Width ?? components.Max(component => component.X + component.Width);
                var height = background?.Height ?? components.Max(component => component.Y + component.Height);
                if (width <= 0 || height <= 0 || width > 16384 || height > 16384) throw new InvalidDataException("Invalid layout dimensions.");
                return new LayoutDocument(sourcePath, layout.Description, width, height, components);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or System.Security.Cryptography.CryptographicException or AggregateException)
            { throw new InvalidDataException("Unable to decode the layout: " + exception.Message, exception); }
            finally { RunLog.Quiet = previousQuiet; }
        }
    }

    private static void ValidateEnvelope(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 148 || bytes.Length > MaximumFileBytes || !bytes.AsSpan(0, 4).SequenceEqual("AACS"u8))
            throw new InvalidDataException("This is not a supported AACS .FML file.");
        var chunks = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
        if (chunks is 0 or > 4096) throw new InvalidDataException("Invalid layout chunk count.");
        long total = 0; var position = 128;
        for (var index = 0u; index < chunks; index++)
        {
            if (position > bytes.Length - 16) throw new InvalidDataException("Truncated layout chunk header.");
            var compressed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4));
            var decoded = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4, 4));
            var next = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 12, 4));
            total += decoded;
            if (compressed == 0 || decoded == 0 || total > MaximumDecodedBytes || compressed > bytes.Length - position - 16 ||
                next != (long)position + 16 + compressed)
                throw new InvalidDataException("Invalid or oversized layout chunk.");
            position = checked((int)next);
        }
        if (position != bytes.Length && position != bytes.Length - 4) throw new InvalidDataException("Unexpected layout trailer.");
    }
}

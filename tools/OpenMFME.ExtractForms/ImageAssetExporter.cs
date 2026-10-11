using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenMFME.Formats;

internal static class ImageAssetExporter
{
    public static void Export(byte[] source, DfmCatalog catalog, string directory)
    {
        Directory.CreateDirectory(directory);
        var entries = new List<object>();
        foreach (var form in catalog.Forms)
        foreach (var node in form.Root.DescendantsAndSelf())
        foreach (var (property, value) in node.Properties.Where(pair => pair.Value.Kind == "Binary"))
        {
            var metadata = value.Value ?? throw new InvalidDataException("Missing binary metadata");
            var offset = int.Parse(metadata["fileOffset"]!.GetValue<string>()[2..], NumberStyles.HexNumber);
            var length = metadata["byteLength"]!.GetValue<int>();
            if (offset < 0 || length < 0 || offset > source.Length - length)
                throw new InvalidDataException("Asset range is outside the source");
            var payload = source.AsSpan(offset, length);
            var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            if (hash != metadata["sha256"]!.GetValue<string>())
                throw new InvalidDataException("Asset hash differs from the catalog");
            string file;
            int? width = null, height = null;
            var frames = 1;
            string status;
            if (property == "Glyph.Data")
            {
                if (payload.Length < 58 || BinaryPrimitives.ReadInt32LittleEndian(payload) != payload.Length - 4 ||
                    !payload.Slice(4, 2).SequenceEqual("BM"u8))
                    throw new InvalidDataException("Unrecognized VCL glyph wrapper");
                var bitmap = payload[4..];
                if (BinaryPrimitives.ReadInt32LittleEndian(bitmap[2..]) != bitmap.Length ||
                    BinaryPrimitives.ReadInt32LittleEndian(bitmap[14..]) != 40 ||
                    BinaryPrimitives.ReadInt32LittleEndian(bitmap[30..]) != 0)
                    throw new InvalidDataException("Unsupported glyph bitmap header");
                width = BinaryPrimitives.ReadInt32LittleEndian(bitmap[18..]);
                height = BinaryPrimitives.ReadInt32LittleEndian(bitmap[22..]);
                frames = (int)node.Number("NumGlyphs", 1);
                if (width <= 0 || height <= 0 || frames < 1 || frames > 4 || width % frames != 0)
                    throw new InvalidDataException("Invalid glyph strip geometry");
                file = hash + ".bmp";
                File.WriteAllBytes(Path.Combine(directory, file), bitmap.ToArray());
                status = "Glyph bitmap; horizontal state strip";
            }
            else
            {
                // Preserve these containers for later decoding; image-list masks and runtime indices are unresolved.
                file = hash + (property == "Icon.Data" ? ".ico" : ".bin");
                File.WriteAllBytes(Path.Combine(directory, file), payload.ToArray());
                status = property == "Icon.Data" ? "Original icon; archived, not used as OpenMFME branding" :
                    "Image-list container; archived, not mapped to runtime images";
            }
            entries.Add(new { form = form.Root.InstanceName, component = node.InstanceName, property,
                sourceOffset = metadata["fileOffset"]!.GetValue<string>(), sourceLength = length,
                sourceSha256 = hash, file, width, height, frames, status });
        }
        File.WriteAllText(Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(new { schemaVersion = 1, catalog.SourceSha256, entries }, DfmCatalog.JsonOptions) + Environment.NewLine);
        Console.WriteLine($"Archived {entries.Count} image payload references to {directory}.");
    }
}

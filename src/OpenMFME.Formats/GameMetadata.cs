using System.Text;

namespace OpenMFME.Formats;

/// <summary>Read-only metadata; filenames are descriptive data, never commands or paths to execute.</summary>
public sealed record GameMetadata(string FilePath, string Name, string System, string Layout, string Protocol, string[] Roms)
{
    public const int MaximumBytes = 256 * 1024;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Path.GetFileNameWithoutExtension(FilePath) : Name;
    public string Summary => $"{DisplayName}\n\nSystem: {Empty(System)}\nProtocol: {Empty(Protocol)}\nLayout: {Empty(Layout)}\nROMs: {(Roms.Length == 0 ? "Not specified" : string.Join(", ", Roms))}\n\nConfiguration: {FilePath}";
    private static string Empty(string value) => value.Length == 0 ? "Not specified" : value;

    public static GameMetadata Parse(string text, string path)
    {
        if (text.Contains('\0')) throw new InvalidDataException("The configuration contains binary data.");
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var roms = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim().TrimStart('\uFEFF');
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';') || trimmed.StartsWith("//"))
                continue;
            var separator = trimmed.IndexOfAny([' ', '\t']);
            if (separator < 0) continue;
            var key = trimmed[..separator];
            var value = trimmed[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            if (key.Equals("ROM", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length > 0) roms.Add(value);
            }
            else if (key.Equals("Game", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Layout", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Protocol", StringComparison.OrdinalIgnoreCase)) fields[key] = value;
        }
        if (fields.Count == 0 && roms.Count == 0) throw new InvalidDataException("No recognized game configuration fields were found.");
        return new GameMetadata(path, fields.GetValueOrDefault("Game", ""), fields.GetValueOrDefault("System", ""),
            fields.GetValueOrDefault("Layout", ""), fields.GetValueOrDefault("Protocol", ""), roms.ToArray());
    }

    public static GameMetadata Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Parse(Decode(ReadBytes(stream)), Path.GetFullPath(path));
    }

    public static async Task<GameMetadata> ReadAsync(Stream stream, string path, CancellationToken token = default)
    {
        using var buffer = new MemoryStream();
        var block = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(block, token)) > 0)
        {
            if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("The configuration is too large to inspect.");
            buffer.Write(block, 0, count);
        }
        return Parse(Decode(buffer.ToArray()), path);
    }

    private static byte[] ReadBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        var block = new byte[4096];
        int count;
        while ((count = stream.Read(block, 0, block.Length)) > 0)
        {
            if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("The configuration is too large to inspect.");
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return Encoding.UTF8.GetString(bytes.AsSpan(3));
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return Encoding.Unicode.GetString(bytes.AsSpan(2));
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return Encoding.BigEndianUnicode.GetString(bytes.AsSpan(2));
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}

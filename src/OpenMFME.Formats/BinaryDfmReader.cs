using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenMFME.Formats;

/// <summary>Reads Delphi TPF0 form streams as data; never instantiates VCL classes or executes handlers.</summary>
public sealed class BinaryDfmReader
{
    private readonly byte[] bytes;
    private readonly int limit;
    private int position;
    private int componentCount;
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    public BinaryDfmReader(byte[] bytes, int offset = 0, int? length = null)
    {
        if (offset < 0 || offset > bytes.Length || length is < 0 || length > bytes.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        this.bytes = bytes;
        position = offset;
        limit = offset + (length ?? bytes.Length - offset);
    }

    public int Position => position;

    public DfmComponent ReadForm()
    {
        if (!Take(4).SequenceEqual("TPF0"u8))
            throw Error("Missing TPF0 signature");
        return ReadComponent(0);
    }

    private DfmComponent ReadComponent(int depth)
    {
        CheckDepth(depth);
        if (++componentCount > 50000)
            throw Error("Component limit exceeded");
        int flags = 0;
        int? childPosition = null;
        if ((Peek() & 0xF0) == 0xF0)
        {
            flags = ReadByte() & 0x0F;
            if ((flags & ~7) != 0)
                throw Error("Unsupported filer flags");
            if ((flags & 2) != 0)
            {
                if (Peek() is not (2 or 3 or 4))
                    throw Error("Invalid child-position value");
                childPosition = checked((int)ReadValue(depth + 1).Number());
            }
        }
        var className = ReadShortString();
        var name = ReadShortString();
        if (!Identifier.IsMatch(className) || (name.Length != 0 && !Identifier.IsMatch(name)))
            throw Error("Invalid component identifier");
        var properties = ReadProperties(depth + 1);
        var children = new List<DfmComponent>();
        while (Peek() != 0)
            children.Add(ReadComponent(depth + 1));
        ReadByte();
        return new DfmComponent
        {
            ClassName = className, InstanceName = name, Flags = flags, ChildPosition = childPosition,
            Properties = properties, Children = children
        };
    }

    private Dictionary<string, DfmValue> ReadProperties(int depth)
    {
        CheckDepth(depth);
        var properties = new Dictionary<string, DfmValue>(StringComparer.Ordinal);
        while (true)
        {
            var name = ReadShortString();
            if (name.Length == 0)
                return properties;
            if (name.Split('.').Any(part => !Identifier.IsMatch(part)))
                throw Error("Invalid property name");
            if (!properties.TryAdd(name, ReadValue(depth + 1)))
                throw Error($"Duplicate property: {name}");
        }
    }

    private DfmValue ReadValue(int depth)
    {
        CheckDepth(depth);
        var type = ReadByte();
        return type switch
        {
            0 => new("Null", null),
            1 => ReadList(depth),
            2 => Scalar("Int8", unchecked((sbyte)ReadByte())),
            3 => Scalar("Int16", BinaryPrimitives.ReadInt16LittleEndian(Take(2))),
            4 => Scalar("Int32", ReadInt32()),
            5 => ReadExtended(),
            6 => Scalar("String", ReadShortString()),
            7 => Scalar("Ident", ReadShortString()),
            8 => Scalar("Boolean", false),
            9 => Scalar("Boolean", true),
            10 => ReadBinary(),
            11 => ReadSet(),
            12 => Scalar("LongString", ReadLengthString(Encoding.Latin1)),
            13 => new("Nil", null),
            14 => ReadCollection(depth),
            15 => Floating("Single", BitConverter.Int32BitsToSingle(ReadInt32())),
            16 => Scalar("Currency", BinaryPrimitives.ReadInt64LittleEndian(Take(8)) / 10000m),
            17 => Floating("Date", ReadDouble()),
            18 => Scalar("WideString", ReadLengthString(Encoding.Unicode, 2)),
            19 => Scalar("Int64", BinaryPrimitives.ReadInt64LittleEndian(Take(8))),
            20 => Scalar("Utf8String", ReadLengthString(new UTF8Encoding(false, true))),
            21 => Floating("Double", ReadDouble()),
            22 => Scalar("UnicodeString", ReadLengthString(Encoding.Unicode, 2)),
            23 => Scalar("UInt64", BinaryPrimitives.ReadUInt64LittleEndian(Take(8))),
            _ => throw Error($"Unsupported Delphi value type {type}")
        };
    }

    private DfmValue ReadList(int depth)
    {
        var items = new JsonArray();
        while (Peek() != 0)
        {
            var value = ReadValue(depth + 1);
            items.Add(new JsonObject { ["kind"] = value.Kind, ["value"] = value.Value });
        }
        ReadByte();
        return new("List", items);
    }

    private DfmValue ReadSet()
    {
        var items = new JsonArray();
        string item;
        while ((item = ReadShortString()).Length != 0)
            items.Add(item);
        return new("Set", items);
    }

    private DfmValue ReadCollection(int depth)
    {
        var items = new JsonArray();
        while (Peek() != 0)
        {
            JsonNode? order = null;
            if (Peek() is 2 or 3 or 4)
                order = ReadValue(depth + 1).Value;
            if (ReadByte() != 1)
                throw Error("Missing collection item list marker");
            var properties = ReadProperties(depth + 1);
            var item = new JsonObject { ["order"] = order, ["properties"] = System.Text.Json.JsonSerializer.SerializeToNode(properties, DfmCatalog.JsonOptions) };
            items.Add(item);
        }
        ReadByte();
        return new("Collection", items);
    }

    private DfmValue ReadBinary()
    {
        var size = ReadInt32();
        var offset = position;
        var payload = Take(size);
        return new("Binary", new JsonObject
        {
            ["byteLength"] = size,
            ["fileOffset"] = $"0x{offset:X8}",
            ["sha256"] = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()
        });
    }

    private DfmValue ReadExtended()
    {
        var raw = Take(10);
        var significand = BinaryPrimitives.ReadUInt64LittleEndian(raw);
        var signExponent = BinaryPrimitives.ReadUInt16LittleEndian(raw[8..]);
        var exponent = signExponent & 0x7FFF;
        double value = exponent == 0x7FFF ? (significand == 0x8000000000000000 ? double.PositiveInfinity : double.NaN)
            : Math.ScaleB((double)significand, (exponent == 0 ? 1 : exponent) - 16383 - 63);
        if ((signExponent & 0x8000) != 0)
            value = -value;
        return Floating("Extended", value);
    }

    private static DfmValue Scalar<T>(string kind, T value) => new(kind, System.Text.Json.JsonSerializer.SerializeToNode(value));
    private static DfmValue Floating(string kind, double value) => double.IsFinite(value) ? Scalar(kind, value) : Scalar(kind, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private double ReadDouble() => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Take(8)));
    private int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    private byte ReadByte() => Take(1)[0];
    private byte Peek() => position < limit ? bytes[position] : throw Error("Unexpected end of stream");
    private string ReadShortString() => Encoding.Latin1.GetString(Take(ReadByte()));
    private string ReadLengthString(Encoding encoding, int bytesPerCharacter = 1)
    {
        var characters = ReadInt32();
        if (characters < 0 || characters > 8 * 1024 * 1024 / bytesPerCharacter)
            throw Error("Invalid string length");
        return encoding.GetString(Take(characters * bytesPerCharacter));
    }
    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > limit - position)
            throw Error("Value exceeds stream bounds");
        var result = bytes.AsSpan(position, count);
        position += count;
        return result;
    }
    private void CheckDepth(int depth)
    {
        if (depth > 128)
            throw Error("Nesting limit exceeded");
    }
    private InvalidDataException Error(string message) => new($"{message} at file offset 0x{position:X8}");
}

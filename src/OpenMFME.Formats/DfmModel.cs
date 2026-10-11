using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenMFME.Formats;

public sealed record DfmValue(string Kind, JsonNode? Value)
{
    public string Text(string fallback = "") => Value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : fallback;
    public double Number(double fallback = 0) =>
        Value is JsonValue scalar && double.TryParse(scalar.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
    public bool Boolean(bool fallback = false) =>
        Value is JsonValue scalar && bool.TryParse(scalar.ToString(), out var value) ? value : fallback;
}

public sealed class DfmComponent
{
    public string ClassName { get; init; } = "";
    public string InstanceName { get; init; } = "";
    public int Flags { get; init; }
    public int? ChildPosition { get; init; }
    public Dictionary<string, DfmValue> Properties { get; init; } = new(StringComparer.Ordinal);
    public List<DfmComponent> Children { get; init; } = [];

    public string Text(string name, string fallback = "") => Properties.TryGetValue(name, out var value) ? value.Text(fallback) : fallback;
    public double Number(string name, double fallback = 0) => Properties.TryGetValue(name, out var value) ? value.Number(fallback) : fallback;
    public bool Boolean(string name, bool fallback = false) => Properties.TryGetValue(name, out var value) ? value.Boolean(fallback) : fallback;
    public IEnumerable<DfmComponent> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var descendant in child.DescendantsAndSelf())
                yield return descendant;
    }
}

public sealed record RecoveredForm(string FileOffset, int ByteLength, DfmComponent Root);
public sealed record ExtractionFailure(string FileOffset, string Error);
public sealed record DfmCatalog(int SchemaVersion, string SourceSha256, int SignatureCount,
    List<RecoveredForm> Forms, List<ExtractionFailure> Failures)
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

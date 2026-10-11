using System.Security.Cryptography;
using System.Text.Json;
using OpenMFME.Formats;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: OpenMFME.ExtractForms <MFME.exe> <output.json> [report.md]");
    return 2;
}
var bytes = File.ReadAllBytes(args[0]);
var forms = new List<RecoveredForm>();
var failures = new List<ExtractionFailure>();
var signatures = 0;
for (var offset = 0; offset <= bytes.Length - 4; offset++)
{
    if (!bytes.AsSpan(offset, 4).SequenceEqual("TPF0"u8))
        continue;
    signatures++;
    try
    {
        var reader = new BinaryDfmReader(bytes, offset);
        var root = reader.ReadForm();
        forms.Add(new RecoveredForm($"0x{offset:X8}", reader.Position - offset, root));
    }
    catch (Exception exception) when (exception is InvalidDataException or System.Text.DecoderFallbackException)
    {
        failures.Add(new ExtractionFailure($"0x{offset:X8}", exception.Message));
    }
}
var catalog = new DfmCatalog(1, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), signatures, forms, failures);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(catalog, DfmCatalog.JsonOptions) + Environment.NewLine);
Console.WriteLine($"Parsed {forms.Count} complete form streams; rejected {failures.Count} signatures.");
Console.WriteLine($"Recovered {forms.Sum(form => form.Root.DescendantsAndSelf().Count())} components.");
foreach (var failure in failures)
    Console.WriteLine($"{failure.FileOffset}: {failure.Error}");
if (args.Length == 3)
{
    var lines = new List<string>
    {
        "# Recovered MFME form streams", "",
        $"Source SHA-256: `{catalog.SourceSha256}`.", "",
        $"{forms.Count} complete streams; {forms.Sum(form => form.Root.DescendantsAndSelf().Count())} components; {failures.Count} rejected signature.", "",
        "This catalog preserves form/control trees, typed properties and event references. Binary payloads are represented by file offsets, lengths and hashes; images are not included. Original handlers are not recovered executable source.", "",
        "| Form | Caption | Components | Event references |",
        "|---|---|---:|---:|"
    };
    foreach (var form in forms)
    {
        var nodes = form.Root.DescendantsAndSelf().ToArray();
        var events = nodes.Sum(node => node.Properties.Keys.Count(key => key.StartsWith("On", StringComparison.Ordinal)));
        lines.Add($"| {form.Root.InstanceName} | {form.Root.Text("Caption").Replace("|", "\\|")} | {nodes.Length} | {events} |");
    }
    lines.AddRange(["", "## Component classes", "", "| Class | Instances |", "|---|---:|"]);
    foreach (var group in forms.SelectMany(form => form.Root.DescendantsAndSelf()).GroupBy(node => node.ClassName).OrderByDescending(group => group.Count()))
        lines.Add($"| {group.Key} | {group.Count()} |");
    lines.AddRange(["", "## Rejected signatures", ""]);
    lines.AddRange(failures.Select(failure => $"- `{failure.FileOffset}`: {failure.Error}"));
    var report = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(report)!);
    File.WriteAllLines(report, lines);
}
return forms.Count == 0 ? 1 : 0;

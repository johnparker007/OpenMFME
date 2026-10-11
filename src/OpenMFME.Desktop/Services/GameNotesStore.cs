using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenMFME.Desktop.Services;

public sealed record GameNotes
{
    public string Name { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public string Technology { get; init; } = "";
    public string MachineType { get; init; } = "";
    public string Stakes { get; init; } = "";
    public string Prizes { get; init; } = "";
    public string Notes { get; init; } = "";
    public string Summary => $"Name: {Name}\nManufacturer: {Manufacturer}\nRelease date: {ReleaseDate}\nTechnology: {Technology}\nMachine type: {MachineType}\nStakes: {Stakes}\nPrizes: {Prizes}\n\n{Notes}";
}

/// <summary>Independent notes keyed by the absolute configuration path, never written into game folders.</summary>
public sealed class GameNotesStore
{
    private sealed record Document(int SchemaVersion, string GamePath, GameNotes Data);
    public const int MaximumBytes = 256 * 1024;
    public const int MaximumComments = 32768;
    public string DirectoryPath { get; }
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public GameNotesStore(string directory) => DirectoryPath = Path.GetFullPath(directory);

    private static string Identity(string gamePath)
    {
        if (!Path.IsPathFullyQualified(gamePath)) throw new InvalidDataException("Game notes require an absolute configuration path.");
        var path = Path.GetFullPath(gamePath);
        return OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    }

    public string FilePathFor(string gamePath) =>
        Path.Combine(DirectoryPath, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Identity(gamePath)))).ToLowerInvariant() + ".json");

    public GameNotes? Read(string gamePath)
    {
        var path = FilePathFor(gamePath);
        // File.Exists hides access errors; opening directly distinguishes missing notes from unreadable notes.
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > MaximumBytes) throw new InvalidDataException("The game-notes file is too large.");
            using var buffer = new MemoryStream();
            var block = new byte[4096];
            int count;
            while ((count = file.Read(block)) > 0)
            {
                if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("The game-notes file is too large.");
                buffer.Write(block, 0, count);
            }
            var document = JsonSerializer.Deserialize<Document>(buffer.ToArray(), Options)
                ?? throw new InvalidDataException("The game-notes file is empty.");
            if (document.SchemaVersion != 1 || document.GamePath is null || Identity(document.GamePath) != Identity(gamePath))
                throw new InvalidDataException("The game-notes version or game path does not match.");
            Validate(document.Data);
            return document.Data;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void Save(string gamePath, GameNotes notes)
    {
        Validate(notes);
        var destination = FilePathFor(gamePath);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, Path.GetFullPath(gamePath), notes), Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("The game notes are too large to save.");
        Directory.CreateDirectory(DirectoryPath);
        var temporary = Path.Combine(DirectoryPath, $".notes-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Validate(GameNotes? notes)
    {
        if (notes is null) throw new InvalidDataException("The game-notes data is missing.");
        var fields = new[] { notes.Name, notes.Manufacturer, notes.ReleaseDate, notes.Technology, notes.MachineType, notes.Stakes, notes.Prizes };
        if (fields.Any(field => field is null || field.Length > 120) || notes.Notes is null || notes.Notes.Length > MaximumComments)
            throw new InvalidDataException("A game-notes field exceeds its supported length.");
    }
}

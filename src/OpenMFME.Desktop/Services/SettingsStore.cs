using System.Text.Json;
using OpenMFME.Formats;

namespace OpenMFME.Desktop.Services;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string[] GameFolders { get; init; } = [];
    public bool IncludeSubfolders { get; init; } = true;
    public bool OpenGameBrowserOnStartup { get; init; }
}

/// <summary>OpenMFME's own preferences. Saving never changes the original application's settings.</summary>
public sealed class SettingsStore
{
    public string FilePath { get; }
    public AppSettings Current { get; private set; } = new();
    public string? LoadNotice { get; private set; }
    public event EventHandler? Changed;
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public SettingsStore(string? path = null)
    {
        FilePath = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenMFME", "settings.json"));
        try
        {
            if (!File.Exists(FilePath)) return;
            if (new FileInfo(FilePath).Length > 128 * 1024) throw new InvalidDataException("The preferences file is too large.");
            Current = Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options)
                ?? throw new InvalidDataException("The preferences file is empty."));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { LoadNotice = $"Preferences could not be loaded; defaults are in use. {exception.Message}"; }
    }

    public void Save(AppSettings settings)
    {
        var normalized = Normalize(settings);
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(normalized, Options) + Environment.NewLine);
            File.Move(temporary, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        Current = normalized;
        LoadNotice = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (settings.SchemaVersion != 1) throw new InvalidDataException("Unsupported preferences version.");
        if (settings.GameFolders is null || settings.GameFolders.Length > 1000)
            throw new InvalidDataException("Invalid game-folder list.");
        var folders = settings.GameFolders.Select(folder =>
        {
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
                throw new InvalidDataException("Game folders must have absolute paths.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        }).Distinct(GameLibraryScanner.PathComparer).ToArray();
        return settings with { GameFolders = folders };
    }
}

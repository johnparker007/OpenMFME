namespace OpenMFME.Formats;

public sealed record GameScanResult(GameMetadata[] Games, string[] Warnings);
public sealed record GameScanProgress(int ConfigurationsVisited, string Folder);

public static class GameLibraryScanner
{
    public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static Task<GameScanResult> ScanAsync(IEnumerable<string> folders, bool recursive, CancellationToken token = default,
        IProgress<GameScanProgress>? progress = null) => Task.Run(() =>
    {
        var games = new List<GameMetadata>();
        var warnings = new List<string>();
        var visited = new HashSet<string>(PathComparer);
        var count = 0;
        foreach (var folder in folders.Distinct(PathComparer))
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(folder)) { warnings.Add($"Folder unavailable: {folder}"); continue; }
            progress?.Report(new GameScanProgress(count, folder));
            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = recursive, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false
                };
                foreach (var file in Directory.EnumerateFiles(folder, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (!file.EndsWith(".gam", StringComparison.OrdinalIgnoreCase)) continue;
                    var fullPath = Path.GetFullPath(file);
                    if (!visited.Add(fullPath)) continue;
                    count++;
                    try { games.Add(GameMetadata.Read(fullPath)); }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                    { warnings.Add($"{Path.GetFileName(file)}: {exception.Message}"); }
                    if (count % 25 == 0) progress?.Report(new GameScanProgress(count, folder));
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            { warnings.Add($"{folder}: {exception.Message}"); }
        }
        token.ThrowIfCancellationRequested();
        return new GameScanResult(games.OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.FilePath, PathComparer).ToArray(), warnings.ToArray());
    }, token);
}

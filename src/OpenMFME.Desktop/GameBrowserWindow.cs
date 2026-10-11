using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using OpenMFME.Desktop.Services;
using OpenMFME.Desktop.Reconstruction;
using OpenMFME.Formats;

namespace OpenMFME.Desktop;

public sealed class GameBrowserWindow : Window
{
    private readonly SettingsStore store;
    private readonly Action<GameMetadata> inspect;
    private readonly Func<Task> preferences;
    private readonly GameNotesStore notesStore;
    private readonly Dictionary<string, GameNotes> gameNotes = new(GameLibraryScanner.PathComparer);
    private GameNotesEditor? notesEditor;
    private MenuItem? editNotesMenu;
    private MenuItem? fileNamesMenu;
    private readonly TextBlock notesDetails = new() { Name = "GameNotesDetails", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { Name = "GameSearch", PlaceholderText = "Search by game, system or filename…" };
    private readonly ComboBox systems = new() { Name = "SystemFilter", MinWidth = 150 };
    private readonly ListBox list;
    private readonly GameManagerTable table;
    private readonly Grid body = new() { Name = "ManagerBody", ColumnDefinitions = new ColumnDefinitions("3*,5,1*") };
    private readonly Dictionary<string, TextBlock> summaryFields = [];
    private bool showFileNames;
    private string sortColumn = "Game Name";
    private bool sortDescending;
    private PixelPoint defaultPosition;
    private readonly TextBlock details = new() { Name = "GameDetails", Text = "Select a game to view its layout information.", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { Name = "BrowserStatus", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock warnings = new() { Name = "ScanWarnings", TextWrapping = TextWrapping.Wrap };
    private readonly Button open = new() { Name = "InspectGame", Content = "Open layout information", IsEnabled = false };
    private readonly Button refresh = new() { Name = "RefreshGames", Content = "Refresh" };
    private readonly Button cancel = new() { Name = "CancelScan", Content = "Stop scan", IsVisible = false };
    private readonly TextBlock folders = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
    private GameMetadata[] games = [];
    private string[] scanWarnings = [];
    private CancellationTokenSource? scan;
    private bool closed;
    private GameManagerFind? findDialog;
    private GameManagerFilter? filterDialog;
    private string nameFilter = "";
    private readonly HashSet<string> excludedPlatforms = new(StringComparer.OrdinalIgnoreCase);
    public Task ScanCompletion { get; private set; } = Task.CompletedTask;

    public GameBrowserWindow(SettingsStore store, Action<GameMetadata> inspect, Func<Task> preferences, GameNotesStore? notesStore = null)
    {
        this.store = store; this.inspect = inspect; this.preferences = preferences;
        this.notesStore = notesStore ?? new GameNotesStore(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "game-notes"));
        Title = "OpenMFME Game Manager"; Width = 1280; Height = 760; MinWidth = 860; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var outer = new DockPanel();
        var definition = RecoveredFormLibrary.Get("ManagerForm").Root.Children.Single(node => node.InstanceName == "ManagerMenu");
        var menu = new Menu { Name = "ManagerMenu", ItemsSource = definition.Children
            .Select(node => RecoveredMenuFactory.CreateItem(node, ResolveMenuAction)).ToArray() };
        editNotesMenu = ((Control[])menu.ItemsSource!).OfType<MenuItem>().Single(item => item.Name == "AddNotes1");
        editNotesMenu.IsEnabled = false;
        DockPanel.SetDock(menu, Dock.Top); outer.Children.Add(menu);
        var viewMenu = ((Control[])menu.ItemsSource!).OfType<MenuItem>().Single(item => item.Name == "Display1");
        fileNamesMenu = viewMenu.ItemsSource!.Cast<Control>().OfType<MenuItem>().Single(item => item.Name == "ShowFileNames1");
        fileNamesMenu.ToggleType = MenuItemToggleType.CheckBox;
        var dock = new DockPanel { Margin = new Thickness(4) };
        outer.Children.Add(dock);
        var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        toolbar.Children.Add(search); Grid.SetColumn(systems, 1); toolbar.Children.Add(systems);
        Grid.SetColumn(refresh, 2); toolbar.Children.Add(refresh); Grid.SetColumn(cancel, 3); toolbar.Children.Add(cancel);
        header.Children.Add(toolbar); header.Children.Add(folders);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        var footer = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        var summary = new Grid { Name = "SelectedGameSummary", ColumnDefinitions = new ColumnDefinitions("100,*"), Margin = new Thickness(4) };
        var fieldNames = new[] { "Name", "Manufacturer", "ReleaseDate", "Technology", "MachineType", "Stakes", "Prizes" };
        var labels = new[] { "Name:", "Manufacturer:", "Release Date:", "Technology:", "Machine Type:", "Stakes:", "Prizes:" };
        for (var index = 0; index < fieldNames.Length; index++)
        {
            summary.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = labels[index], FontSize = 11 };
            Grid.SetRow(label, index); summary.Children.Add(label);
            var value = new TextBlock { Name = "Summary" + fieldNames[index], FontSize = 11, FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis };
            summaryFields[fieldNames[index]] = value;
            Grid.SetRow(value, index); Grid.SetColumn(value, 1); summary.Children.Add(value);
        }
        footer.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#BCC2C9")), BorderThickness = new Thickness(0, 1, 0, 0), Child = summary });
        footer.Children.Add(new Expander { Name = "ScanDetails", Header = "Scan details", Content = new ScrollViewer { MaxHeight = 100, Content = warnings } });
        var statusRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        status.FontSize = 11; status.VerticalAlignment = VerticalAlignment.Center;
        statusRow.Children.Add(status); Grid.SetColumn(open, 1); statusRow.Children.Add(open);
        footer.Children.Add(statusRow); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        table = new GameManagerTable(game => showFileNames ? Path.GetFileName(game.FilePath) : DisplayName(game), SortBy);
        list = table.List; table.UpdateSort(sortColumn, sortDescending);
        body.Children.Add(table);
        var splitter = new GridSplitter { Name = "ManagerSplitter", ResizeDirection = GridResizeDirection.Columns,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.LightGray };
        Grid.SetColumn(splitter, 1); body.Children.Add(splitter);
        var tabs = new TabControl { Name = "GameInformationTabs", ItemsSource = new[]
        {
            new TabItem { Header = "Game", Content = new ScrollViewer { Content = details } },
            new TabItem { Header = "Cabinet", Content = new TextBlock { Name = "CabinetPreviewStatus", Text = "Cabinet artwork is not loaded in this build.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) } },
            new TabItem { Header = "Flyer", Content = new TextBlock { Name = "FlyerPreviewStatus", Text = "Flyer artwork is not loaded in this build.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) } },
            new TabItem { Header = "Notes", Content = new ScrollViewer { Content = notesDetails } }
        } };
        RecoveredFormFactory.AddCompactStyles(tabs);
        foreach (var tab in tabs.ItemsSource!.Cast<TabItem>()) { tab.FontSize = 11; tab.Padding = new Thickness(8, 3); }
        details.Margin = notesDetails.Margin = new Thickness(8); details.FontSize = notesDetails.FontSize = 12;
        Grid.SetColumn(tabs, 2); body.Children.Add(tabs); dock.Children.Add(body); Content = outer;
        search.TextChanged += (_, _) => Filter();
        systems.SelectionChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) =>
        {
            open.IsEnabled = list.SelectedItem is GameMetadata;
            editNotesMenu.IsEnabled = list.SelectedItem is GameMetadata;
            details.Text = list.SelectedItem is GameMetadata game ? game.Summary : "Select a game to view its layout information.";
            UpdateNotesDetails();
        };
        open.Click += (_, _) => InspectSelection();
        list.DoubleTapped += (_, _) => InspectSelection();
        refresh.Click += (_, _) => BeginScan();
        cancel.Click += (_, _) => scan?.Cancel();
        store.Changed += SettingsChanged;
        Opened += (_, _) => { defaultPosition = Position; BeginScan(); };
        Closed += (_, _) => { closed = true; store.Changed -= SettingsChanged; scan?.Cancel(); };
        Filter(); UpdateNotesDetails();
    }

    private Action? ResolveMenuAction(DfmComponent item) => item.Text("OnClick") switch
    {
        "ExitManagerClick" => Close,
        "ShowFileNames1Click" => ToggleFileNames,
        "ResetWindow1Click" => ResetWindow,
        "ResetColumns1Click" => () => table.ResetColumns(),
        "AddNotes1Click" => OpenNotes,
        "FindClick" => OpenFind,
        "FilterClick" => OpenFilter,
        "ScanDirectoryClick" => BeginScan,
        "SetGameDirectoryClick" => async () => await preferences(),
        "SetMainFolder1Click" => async () => await new PreferencesWindow(store, mainFolderOnly: true).ShowDialog<bool>(this),
        _ => null
    };

    private string DisplayName(GameMetadata game) => gameNotes.TryGetValue(game.FilePath, out var notes) && !string.IsNullOrWhiteSpace(notes.Name)
        ? notes.Name : game.DisplayName;

    private void ToggleFileNames()
    {
        showFileNames = !showFileNames;
        table.UpdateNameHeader(showFileNames);
        // Recreate rows so a name-mode change also refreshes already realized cells.
        fileNamesMenu!.IsChecked = showFileNames;
        table.RefreshRows();
        Filter();
    }

    private void ResetWindow()
    {
        WindowState = WindowState.Normal; Width = 1280; Height = 760; Position = defaultPosition;
        body.ColumnDefinitions[0].Width = new GridLength(3, GridUnitType.Star);
        body.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
    }

    private void SortBy(string column)
    {
        if (sortColumn == column) sortDescending = !sortDescending;
        else { sortColumn = column; sortDescending = false; }
        table.UpdateSort(sortColumn, sortDescending); Filter();
    }

    private void UpdateNotesDetails()
    {
        var game = list.SelectedItem as GameMetadata;
        var notes = game is not null ? gameNotes.GetValueOrDefault(game.FilePath) : null;
        notesDetails.Text = game is null ? "Select a game to view its notes."
            : notes?.Summary ?? "No OpenMFME notes saved for this game.";
        var values = game is null ? new string[7] : new[] { DisplayName(game), notes?.Manufacturer ?? "", notes?.ReleaseDate ?? "",
            string.IsNullOrWhiteSpace(notes?.Technology) ? game.System : notes.Technology,
            notes?.MachineType ?? "", notes?.Stakes ?? "", notes?.Prizes ?? "" };
        foreach (var pair in summaryFields.Zip(values)) { pair.First.Value.Text = pair.Second; ToolTip.SetTip(pair.First.Value, pair.Second); }
    }

    private void OpenNotes()
    {
        if (notesEditor is not null) { notesEditor.Window.Activate(); return; }
        if (list.SelectedItem is not GameMetadata game) return;
        notesEditor = new GameNotesEditor(notesStore, game, notes =>
        { gameNotes[game.FilePath] = notes; Filter(); UpdateNotesDetails(); BeginScan(); });
        notesEditor.Window.Closed += (_, _) => notesEditor = null;
        notesEditor.Window.ShowDialog(this);
    }

    private void OpenFind()
    {
        if (findDialog is not null) { findDialog.Window.Activate(); return; }
        findDialog = new GameManagerFind(FindNext);
        findDialog.Window.Closed += (_, _) => findDialog = null;
        findDialog.Window.Show(this);
    }

    private bool FindNext(string query)
    {
        var visible = list.ItemsSource!.Cast<GameMetadata>().ToArray();
        if (visible.Length == 0) return false;
        var start = Array.IndexOf(visible, list.SelectedItem as GameMetadata);
        for (var offset = 1; offset <= visible.Length; offset++)
        {
            var candidate = visible[(start + offset) % visible.Length];
            if (!DisplayName(candidate).Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            list.SelectedItem = candidate; list.ScrollIntoView(candidate); return true;
        }
        return false;
    }

    private void OpenFilter()
    {
        if (filterDialog is not null) { filterDialog.Window.Activate(); return; }
        filterDialog = new GameManagerFilter(nameFilter, excludedPlatforms,
            value => { nameFilter = value; Filter(); }, Filter, BeginScan);
        filterDialog.UpdatePlatforms(games.Select(game => game.System));
        filterDialog.Window.Closed += (_, _) => filterDialog = null;
        filterDialog.Window.Show(this);
    }

    private void SettingsChanged(object? sender, EventArgs e) => BeginScan();
    private void BeginScan() => ScanCompletion = ReloadAsync();

    private async Task ReloadAsync()
    {
        scan?.Cancel();
        using var current = new CancellationTokenSource();
        scan = current;
        var token = current.Token;
        refresh.IsEnabled = false; cancel.IsVisible = true;
        folders.Text = store.Current.GameFolders.Length == 0 ? "No game folders selected. Choose Database > Set Main Game Folder to get started."
            : $"{store.Current.GameFolders.Length} game folder(s) · {(store.Current.IncludeSubfolders ? "Including subfolders" : "Top folders only")}";
        status.Text = "Looking for games…";
        try
        {
            var progress = new Progress<GameScanProgress>(value => Dispatcher.UIThread.Post(() =>
            {
                if (!closed && ReferenceEquals(scan, current) && !token.IsCancellationRequested)
                    status.Text = $"Looking for games… {value.ConfigurationsVisited:N0} configurations checked.";
            }));
            var result = await GameLibraryScanner.ScanAsync(store.Current.GameFolders, store.Current.IncludeSubfolders, token, progress);
            if (closed || !ReferenceEquals(scan, current)) return;
            var notesResult = await Task.Run(() =>
            {
                var loaded = new Dictionary<string, GameNotes>(GameLibraryScanner.PathComparer);
                var issues = new List<string>();
                foreach (var game in result.Games)
                {
                    token.ThrowIfCancellationRequested();
                    try { if (this.notesStore.Read(game.FilePath) is { } notes) loaded[game.FilePath] = notes; }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
                    { issues.Add($"Notes for {game.DisplayName}: {exception.Message}"); }
                }
                return (loaded, issues);
            }, token);
            if (closed || !ReferenceEquals(scan, current)) return;
            gameNotes.Clear(); foreach (var pair in notesResult.loaded) gameNotes.Add(pair.Key, pair.Value);
            games = result.Games; scanWarnings = result.Warnings.Concat(notesResult.issues).ToArray();
            filterDialog?.UpdatePlatforms(games.Select(game => game.System));
            warnings.Text = scanWarnings.Length == 0 ? "No issues reported. Inaccessible subfolders and directory links are skipped."
                : string.Join(Environment.NewLine, scanWarnings.Take(100)) + (scanWarnings.Length > 100 ? "\nFurther issues omitted from this view." : "");
            var previousSystem = systems.SelectedItem as string;
            systems.ItemsSource = new[] { "All systems" }.Concat(games.Select(game => game.System).Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)).ToArray();
            systems.SelectedItem = previousSystem is not null && ((string[])systems.ItemsSource).Contains(previousSystem)
                ? previousSystem : "All systems";
            Filter();
        }
        catch (OperationCanceledException)
        { if (!closed && ReferenceEquals(scan, current)) { Filter(); status.Text = "Scan stopped. Previous results are still shown."; } }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!closed && ReferenceEquals(scan, current)) status.Text = $"Unable to scan game folders: {exception.Message}"; }
        finally
        {
            if (ReferenceEquals(scan, current))
            { scan = null; if (!closed) { refresh.IsEnabled = true; cancel.IsVisible = false; } }
        }
    }

    private void Filter()
    {
        var query = search.Text?.Trim() ?? "";
        var system = systems.SelectedItem as string;
        var selected = list.SelectedItem as GameMetadata;
        var visible = games.Where(game => !excludedPlatforms.Contains(game.System) &&
            DisplayName(game).Contains(nameFilter, StringComparison.OrdinalIgnoreCase) && (system is null or "All systems" || game.System.Equals(system, StringComparison.OrdinalIgnoreCase)) &&
            $"{DisplayName(game)} {game.System} {game.FilePath}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        Func<GameMetadata, string> key = sortColumn == "Tech" ? game => game.System
            : game => showFileNames ? Path.GetFileName(game.FilePath) : DisplayName(game);
        visible = (sortDescending ? visible.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)
            : visible.OrderBy(key, StringComparer.OrdinalIgnoreCase)).ThenBy(game => game.FilePath, GameLibraryScanner.PathComparer).ToArray();
        list.ItemsSource = visible;
        if (selected is not null) list.SelectedItem = visible.FirstOrDefault(game => GameLibraryScanner.PathComparer.Equals(game.FilePath, selected.FilePath));
        status.Text = $"{visible.Length:N0} of {games.Length:N0} games" + (scanWarnings.Length == 0 ? "" : $" · {scanWarnings.Length:N0} file/folder issues");
    }

    private void InspectSelection()
    {
        if (list.SelectedItem is not GameMetadata game) return;
        inspect(game); Close();
    }
}

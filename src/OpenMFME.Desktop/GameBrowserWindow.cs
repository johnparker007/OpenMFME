using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using OpenMFME.Desktop.Services;
using OpenMFME.Desktop.Reconstruction;
using OpenMFME.Formats;

namespace OpenMFME.Desktop;

public sealed class GameBrowserWindow : Window
{
    private readonly SettingsStore store;
    private readonly Action<GameMetadata> inspect;
    private readonly Func<Task> preferences;
    private readonly TextBox search = new() { Name = "GameSearch", PlaceholderText = "Search by game, system or filename…" };
    private readonly ComboBox systems = new() { Name = "SystemFilter", MinWidth = 150 };
    private readonly ListBox list = new() { Name = "GamesList" };
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

    public GameBrowserWindow(SettingsStore store, Action<GameMetadata> inspect, Func<Task> preferences)
    {
        this.store = store; this.inspect = inspect; this.preferences = preferences;
        Title = "OpenMFME Game Manager"; Width = 960; Height = 650; MinWidth = 720; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var outer = new DockPanel();
        var definition = RecoveredFormLibrary.Get("ManagerForm").Root.Children.Single(node => node.InstanceName == "ManagerMenu");
        var menu = new Menu { Name = "ManagerMenu", ItemsSource = definition.Children
            .Select(node => RecoveredMenuFactory.CreateItem(node, ResolveMenuAction)).ToArray() };
        DockPanel.SetDock(menu, Dock.Top); outer.Children.Add(menu);
        var dock = new DockPanel { Margin = new Thickness(20) };
        outer.Children.Add(dock);
        var header = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = "Game Manager", FontSize = 22, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = "Browse layout information. Games cannot be played in this build.", TextWrapping = TextWrapping.Wrap });
        header.Children.Add(folders);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        toolbar.Children.Add(refresh); toolbar.Children.Add(cancel); header.Children.Add(toolbar);
        var filter = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        filter.Children.Add(search); Grid.SetColumn(systems, 1); filter.Children.Add(systems); header.Children.Add(filter);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(status);
        footer.Children.Add(new Expander { Header = "Scan details", Content = new ScrollViewer { MaxHeight = 100, Content = warnings } });
        DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        body.Children.Add(list);
        var information = new DockPanel();
        DockPanel.SetDock(open, Dock.Bottom); information.Children.Add(open);
        information.Children.Add(new ScrollViewer { Content = details }); Grid.SetColumn(information, 1); body.Children.Add(information);
        dock.Children.Add(body); Content = outer;
        var rows = new Style(selector => selector.OfType<ListBoxItem>());
        rows.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        list.Styles.Add(rows);
        list.ItemTemplate = new FuncDataTemplate<GameMetadata>((game, _) =>
        {
            if (game is null) return null;
            var row = new StackPanel { Spacing = 3, Margin = new Thickness(0, 3) };
            row.Children.Add(new TextBlock { Text = game.DisplayName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = game.System.Length == 0 ? "System not specified" : game.System, FontSize = 12, Foreground = Brushes.DimGray });
            var filename = new TextBlock { Text = game.FilePath, FontSize = 11, Foreground = Brushes.DimGray, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(filename, game.FilePath); row.Children.Add(filename);
            return row;
        });
        search.TextChanged += (_, _) => Filter();
        systems.SelectionChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) =>
        {
            open.IsEnabled = list.SelectedItem is GameMetadata;
            details.Text = list.SelectedItem is GameMetadata game ? game.Summary : "Select a game to view its layout information.";
        };
        open.Click += (_, _) => InspectSelection();
        list.DoubleTapped += (_, _) => InspectSelection();
        refresh.Click += (_, _) => BeginScan();
        cancel.Click += (_, _) => scan?.Cancel();
        store.Changed += SettingsChanged;
        Opened += (_, _) => BeginScan();
        Closed += (_, _) => { closed = true; store.Changed -= SettingsChanged; scan?.Cancel(); };
        Filter();
    }

    private Action? ResolveMenuAction(DfmComponent item) => item.Text("OnClick") switch
    {
        "ExitManagerClick" => Close,
        "FindClick" => OpenFind,
        "FilterClick" => OpenFilter,
        "ScanDirectoryClick" => BeginScan,
        "SetGameDirectoryClick" => async () => await preferences(),
        "SetMainFolder1Click" => async () => await new PreferencesWindow(store, mainFolderOnly: true).ShowDialog<bool>(this),
        _ => null
    };

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
            if (!candidate.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
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
            games = result.Games; scanWarnings = result.Warnings;
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
            game.DisplayName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase) && (system is null or "All systems" || game.System.Equals(system, StringComparison.OrdinalIgnoreCase)) &&
            $"{game.DisplayName} {game.System} {game.FilePath}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
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

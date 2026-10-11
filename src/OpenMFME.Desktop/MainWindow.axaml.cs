using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenMFME.Desktop.Reconstruction;
using OpenMFME.Formats;
using OpenMFME.Desktop.Services;

namespace OpenMFME.Desktop;

public partial class MainWindow : Window
{
    private readonly SettingsStore settings;
    private GameBrowserWindow? gameBrowser;
    private PreferencesWindow? preferencesWindow;

    public MainWindow() : this(new SettingsStore()) { }

    public MainWindow(SettingsStore settings)
    {
        this.settings = settings;
        InitializeComponent();
        var original = RecoveredFormLibrary.Get("MainForm").Root;
        var definition = original.Children.Single(child => child.InstanceName == "MainMenu");
        var items = definition.Children.Select(node => RecoveredMenuFactory.CreateItem(node, ResolveMenuAction)).ToList();
        var reconstruction = new MenuItem { Header = "UI reconstruction" };
        var browse = new MenuItem { Header = "Browse all recovered forms…" };
        browse.Click += (_, _) => new FormBrowserWindow().Show(this);
        reconstruction.ItemsSource = new[] { browse };
        items.Add(reconstruction);
        MainMenuBar.ItemsSource = items;
        var meters = original.Children.Single(child => child.InstanceName == "MeterPanel");
        MeterStrip.Content = new RecoveredFormFactory().CreateSurface(meters, inheritanceRoot: original);
        RecoveredFormFactory.AddCompactStyles(MeterStrip);
        Opened += (_, _) =>
        {
            if (settings.LoadNotice is not null) StatusText.Text = settings.LoadNotice;
            if (settings.Current.OpenGameBrowserOnStartup) OpenGameBrowser();
        };
    }

    private Action? ResolveMenuAction(DfmComponent item)
    {
        var handler = item.Text("OnClick");
        if (handler == "GeneralClick") return () => _ = OpenPreferencesAsync();
        if (handler == "GameManager1Click") return OpenGameBrowser;
        if (handler == "LoadGame1Click")
            return () => OpenLayout_Click(this, new RoutedEventArgs());
        if (handler is "Exit1Click" or "ExitWithoutSave1Click")
            return Close;
        if (handler == "About1Click")
            return () => About_Click(this, new RoutedEventArgs());
        if (handler == "FullScreen1Click")
            return () => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        var target = handler switch
        {
            "ConfigurationClick" => "Config",
            "MainBoard1Click" => "DebugForm",
            "Samples1Click" => "Sampler",
            "SEC1Click" => "SECForm",
            "Notes1Click" => "LayoutNotes",
            "Diag1Click" => "DataWindow",
            "SnapToGridClick" => "SnapForm",
            "PlatformClick" => "PlatformForm",
            "GroupBrowser1Click" => "GroupBrowser",
            "ReelEditor1Click" => "ReelEditorForm",
            "CharacterEditor1Click" => "ChrEditor",
            "CheckFonts1Click" => "FontCheckForm",
            "AutoPlay1Click" => "AutoPlayForm",
            "ComponentList1Click" => "ComponentsForm",
            "EMEditor1Click" => "EMEditorForm",
            "GraphicEditorClick" => "GraphicEditorForm",
            "PictureViewerClick" => "PreviewForm",
            "LampImageEditor1Click" => "LampExportForm",
            "LampCreator1Click" => "LampCreatorForm",
            "SpriteViewer1Click" => "SpriteViewForm",
            "OKISoundROMeditor1Click" => "OKIEditorForm",
            "MaygayM1FTU1Click" => "FTUForm",
            "A5VideoViewer1Click" => "VideoViewerForm",
            _ => null
        };
        return target is null ? null : () => new RecoveredFormFactory().CreateWindow(RecoveredFormLibrary.Get(target)).Show(this);
    }

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    private async void OpenLayout_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open MFME layout information",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("MFME game configuration") { Patterns = ["*.gam"] }]
            });
            if (selected.Count == 0)
                return;

            await using var stream = await selected[0].OpenReadAsync();
            var information = await GameMetadata.ReadAsync(stream, selected[0].TryGetLocalPath() ?? selected[0].Name);
            InspectGame(information);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            await ShowMessage("Unable to open layout", exception.Message);
        }
    }

    private void InspectGame(GameMetadata game)
    {
        LayoutSummary.Text = game.Summary;
        StatusText.Text = $"Inspecting {game.DisplayName} · Emulation not connected";
        Activate();
    }

    private async Task OpenPreferencesAsync(Window? owner = null)
    {
        if (preferencesWindow is not null) { preferencesWindow.Activate(); return; }
        var dialog = new PreferencesWindow(settings);
        preferencesWindow = dialog;
        try { await dialog.ShowDialog<bool>(owner ?? this); }
        finally { preferencesWindow = null; }
    }

    private void OpenGameBrowser()
    {
        if (gameBrowser is not null) { gameBrowser.Activate(); return; }
        gameBrowser = new GameBrowserWindow(settings, InspectGame, () => OpenPreferencesAsync(gameBrowser));
        gameBrowser.Closed += (_, _) => gameBrowser = null;
        gameBrowser.Show(this);
    }

    private void Games_Click(object? sender, RoutedEventArgs e) => OpenGameBrowser();
    private async void Preferences_Click(object? sender, RoutedEventArgs e) => await OpenPreferencesAsync();

    private void Forms_Click(object? sender, RoutedEventArgs e) => new FormBrowserWindow().Show(this);

    private async void About_Click(object? sender, RoutedEventArgs e) =>
        await ShowMessage("About OpenMFME",
            "OpenMFME\n\nAn independent reconstruction of MFME for fruit machine preservation.\n\nRecovered menus and previews of 66 original forms, with saved preferences and a game browser. Emulation is not yet implemented.");

    private async Task ShowMessage(string title, string message)
    {
        var close = new Button { Content = "Close", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 20,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    close
                }
            }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
}

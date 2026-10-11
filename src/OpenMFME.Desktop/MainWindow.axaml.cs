using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenMFME.Desktop.Reconstruction;
using OpenMFME.Formats;
using OpenMFME.Desktop.Services;
using OpenMFME.LayoutDecoder;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;

namespace OpenMFME.Desktop;

public partial class MainWindow : Window
{
    private readonly SettingsStore settings;
    private GameBrowserWindow? gameBrowser;
    private LayoutSurface? layoutSurface;
    private CancellationTokenSource? layoutCancellation;
    public GameMetadata? SelectedGame { get; private set; }
    public LayoutDocument? LoadedLayout { get; private set; }
    public Task LayoutLoadCompletion { get; private set; } = Task.CompletedTask;
    public string? LayoutLoadError { get; private set; }

    public LayoutPreviewWindow? LayoutPreview { get; private set; }
    private MenuItem? layoutInspector;
    private MenuItem? renderTestMenu;
    private readonly DispatcherTimer renderTestTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch renderTestClock = new();
    public bool IsRenderTestRunning => renderTestTimer.IsEnabled;
    private bool startupHandled;
    private bool closing;
    public GameBrowserWindow? GameManager => gameBrowser;

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
        var inspect = new MenuItem { Header = "Inspect loaded layout…", IsEnabled = false };
        inspect.Click += (_, _) => { if (LoadedLayout is { } layout) PreviewLayout(layout.FilePath); };
        layoutInspector = inspect;
        renderTestMenu = new MenuItem { Name = "RenderTest", Header = "Animate reels and lamps (test)", IsEnabled = false,
            ToggleType = MenuItemToggleType.CheckBox };
        renderTestMenu.Click += (_, _) => SetRenderTest(!IsRenderTestRunning);
        renderTestTimer.Tick += (_, _) => layoutSurface?.ApplyRenderTest(renderTestClock.Elapsed.TotalSeconds);
        reconstruction.ItemsSource = new[] { browse, inspect, renderTestMenu };
        items.Add(reconstruction);
        MainMenuBar.ItemsSource = items;
        var meters = original.Children.Single(child => child.InstanceName == "MeterPanel");
        MeterStrip.Content = new RecoveredFormFactory().CreateSurface(meters, inheritanceRoot: original);
        RecoveredFormFactory.AddCompactStyles(MeterStrip);
        Opened += (_, _) =>
        {
            if (startupHandled) return;
            startupHandled = true;
            if (settings.LoadNotice is not null) _ = ShowMessage("OpenMFME preferences", settings.LoadNotice);
            if (settings.Current.OpenGameBrowserOnStartup) OpenGameBrowser();
        };
        Closing += (_, _) =>
        {
            SetRenderTest(false); closing = true; gameBrowser?.Close(); layoutCancellation?.Cancel();
            LayoutHost.Child = null; layoutSurface?.Dispose(); layoutSurface = null;
        };
    }

    private Action? ResolveMenuAction(DfmComponent item)
    {
        var handler = item.Text("OnClick");
        if (handler == "GameManager1Click") return OpenGameBrowser;
        if (handler == "LoadLayoutClick") return () => OpenFml_Click(this, new RoutedEventArgs());
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
            "GeneralClick" => "GeneralConfig",
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
                Title = "Load MFME game",
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
        SelectedGame = game;
        Activate();
        if (string.IsNullOrWhiteSpace(game.Layout))
        {
            LayoutLoadError = "The game configuration does not name a layout.";
            _ = ShowMessage("Unable to load layout", LayoutLoadError);
            return;
        }
        try { LoadLayout(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(game.FilePath)!, game.Layout)), game.DisplayName); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        { LayoutLoadError = exception.Message; _ = ShowMessage("Unable to locate layout", exception.Message); }
    }

    public void LoadLayout(string path, string? gameTitle = null)
    {
        layoutCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        layoutCancellation = cancellation;
        LayoutLoadError = null;
        LayoutLoadCompletion = LoadLayoutAsync(path, gameTitle, cancellation);
    }

    private async Task LoadLayoutAsync(string path, string? gameTitle, CancellationTokenSource cancellation)
    {
        try
        {
            var document = await Task.Run(() => FmlLayoutReader.Read(path, cancellation.Token), cancellation.Token);
            if (closing || cancellation.IsCancellationRequested) return;
            var surface = new LayoutSurface(document);
            SetRenderTest(false);
            LayoutHost.Child = null;
            layoutSurface?.Dispose(); layoutSurface = surface;
            LayoutHost.Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Child = surface.Canvas
            };
            LoadedLayout = document;
            Title = gameTitle ?? Path.GetFileNameWithoutExtension(path);
            layoutInspector!.IsEnabled = true;
            renderTestMenu!.IsEnabled = surface.HasAnimatedControls;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        {
            if (!closing && !cancellation.IsCancellationRequested)
            {
                LayoutLoadError = exception.Message;
                _ = ShowMessage("Unable to load layout", exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(layoutCancellation, cancellation)) layoutCancellation = null;
            cancellation.Dispose();
        }
    }

    public void SetRenderTest(bool enabled)
    {
        if (enabled && (closing || layoutSurface?.HasAnimatedControls != true)) return;
        renderTestMenu!.IsChecked = enabled;
        if (enabled)
        {
            renderTestClock.Restart(); layoutSurface!.ApplyRenderTest(0); renderTestTimer.Start();
        }
        else
        {
            renderTestTimer.Stop(); renderTestClock.Reset(); layoutSurface?.ResetOutputs();
        }
    }

    public void PreviewLayout(string path)
    {
        if (LayoutPreview is not null) { LayoutPreview.Close(); }
        var preview = new LayoutPreviewWindow(path);
        LayoutPreview = preview;
        preview.Closed += (_, _) => { if (ReferenceEquals(LayoutPreview, preview)) LayoutPreview = null; };
        preview.Show(this);
    }

    private async void OpenFml_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load MFME v20.1 layout", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("MFME layout") { Patterns = ["*.fml"] }]
            });
            if (selected.Count > 0 && selected[0].TryGetLocalPath() is { } path) LoadLayout(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { await ShowMessage("Unable to preview layout", exception.Message); }
    }

    private void OpenGameBrowser()
    {
        if (gameBrowser is not null) { gameBrowser.Activate(); return; }
        var manager = new GameBrowserWindow(settings, InspectGame, async () =>
            await new PreferencesWindow(settings).ShowDialog<bool>(gameBrowser!));
        gameBrowser = manager;
        manager.Closed += (_, _) =>
        {
            gameBrowser = null;
            if (!closing) { Show(); Activate(); }
        };
        try { SetRenderTest(false); manager.Show(); Hide(); }
        catch { gameBrowser = null; manager.Close(); Show(); throw; }
    }

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

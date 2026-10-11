using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenMFME.Desktop.Reconstruction;
using OpenMFME.Formats;

namespace OpenMFME.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
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
        MeterStrip.Content = new RecoveredFormFactory().CreateSurface(meters);
        RecoveredFormFactory.AddCompactStyles(MeterStrip);
    }

    private Action? ResolveMenuAction(DfmComponent item)
    {
        var handler = item.Text("OnClick");
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
            "GameManager1Click" => "ManagerForm",
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
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            var fields = text.Split('\n')
                .Select(line => line.Trim())
                .Where(line => new[] { "Game ", "System ", "Layout ", "ROM ", "Protocol " }
                    .Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
            var information = string.Join(Environment.NewLine + Environment.NewLine, fields);
            LayoutSummary.Text = string.IsNullOrWhiteSpace(information)
                ? "No recognized game configuration fields were found."
                : information;
            StatusText.Text = $"Inspecting {selected[0].Name} · Emulation not connected";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowMessage("Unable to open layout", exception.Message);
        }
    }

    private void Forms_Click(object? sender, RoutedEventArgs e) => new FormBrowserWindow().Show(this);

    private async void About_Click(object? sender, RoutedEventArgs e) =>
        await ShowMessage("About OpenMFME",
            "OpenMFME\n\nAn independent reconstruction of MFME for fruit machine preservation.\n\nRecovered menus and first-pass previews of 66 original forms. Custom graphics and original commands are still being reconstructed. Emulation is not yet implemented.");

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

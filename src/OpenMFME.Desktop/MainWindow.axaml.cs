using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace OpenMFME.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

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

    private async void Forms_Click(object? sender, RoutedEventArgs e)
    {
        using var stream = AssetLoader.Open(new Uri("avares://OpenMFME.Desktop/Assets/form-inventory.json"));
        using var inventory = await JsonDocument.ParseAsync(stream);
        var entries = inventory.RootElement.GetProperty("forms").EnumerateArray()
            .Select(form => $"{form.GetProperty("instanceName").GetString()}  ({form.GetProperty("className").GetString()})")
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var dialog = new Window
        {
            Title = "Recovered form inventory",
            Width = 620,
            Height = 560,
            MinWidth = 420,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    Docked(new TextBlock
                    {
                        Text = $"{entries.Length} named form headers\nThese windows are inventoried; their controls and behavior still need reconstruction.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 16)
                    }),
                    new ListBox { ItemsSource = entries }
                }
            }
        };
        await dialog.ShowDialog(this);
    }

    private static Control Docked(Control control)
    {
        DockPanel.SetDock(control, Dock.Top);
        return control;
    }

    private async void About_Click(object? sender, RoutedEventArgs e) =>
        await ShowMessage("About OpenMFME",
            "OpenMFME\n\nAn independent reconstruction of MFME for fruit machine preservation.\n\nInitial milestone: a runnable C# and Avalonia desktop shell. Original dialogs and emulation are not yet implemented.");

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

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenMFME.LayoutDecoder;

namespace OpenMFME.Desktop;

public sealed class LayoutPreviewWindow : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<Bitmap> bitmaps = [];
    private readonly Dictionary<int, Border> outlines = [];
    private readonly Border viewport = new() { Name = "LayoutViewport", Background = Brushes.DimGray };
    private readonly ListBox components = new() { Name = "LayoutComponents" };
    private readonly TextBlock properties = new() { Name = "LayoutComponentDetails", TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly TextBlock status = new() { Name = "LayoutPreviewStatus", Text = "Loading layout…", TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox zoom = new() { Name = "LayoutZoom", ItemsSource = new[] { "Fit", "100%" }, SelectedIndex = 0, Width = 90 };
    private readonly CheckBox showBounds = new() { Name = "ShowComponentBounds", Content = "Component outlines", IsChecked = true };
    private Canvas? canvas;
    private bool closed;
    public LayoutDocument? Document { get; private set; }
    public Task LoadCompletion { get; private set; } = Task.CompletedTask;

    public LayoutPreviewWindow(string path)
    {
        Title = "OpenMFME layout preview — " + Path.GetFileName(path);
        Width = 1080; Height = 800; MinWidth = 760; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var dock = new DockPanel { Margin = new Thickness(8) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(zoom); toolbar.Children.Add(showBounds);
        toolbar.Children.Add(new TextBlock { Text = "Static layout preview · Emulation is not connected", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        DockPanel.SetDock(toolbar, Dock.Top); dock.Children.Add(toolbar);
        DockPanel.SetDock(status, Dock.Bottom); status.Margin = new Thickness(0, 8, 0, 0); dock.Children.Add(status);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300"), ColumnSpacing = 8 };
        body.Children.Add(viewport);
        var inspector = new Grid { RowDefinitions = new RowDefinitions("*,*"), RowSpacing = 8 };
        components.ItemTemplate = new FuncDataTemplate<LayoutComponent>((component, _) => component is null ? null
            : new TextBlock { Text = $"#{component.Index} {component.Type} · {component.Number}", FontSize = 11 });
        inspector.Children.Add(components);
        var info = new ScrollViewer { Content = properties }; Grid.SetRow(info, 1); inspector.Children.Add(info);
        Grid.SetColumn(inspector, 1); body.Children.Add(inspector); dock.Children.Add(body); Content = dock;
        zoom.SelectionChanged += (_, _) => SetZoom();
        showBounds.IsCheckedChanged += (_, _) => UpdateOutlines();
        components.SelectionChanged += (_, _) =>
        {
            properties.Text = components.SelectedItem is LayoutComponent component
                ? component.Properties.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) : "";
            UpdateOutlines();
        };
        Opened += (_, _) => LoadCompletion = LoadAsync(path);
        Closed += (_, _) =>
        {
            closed = true; cancellation.Cancel();
            viewport.Child = null;
            foreach (var bitmap in bitmaps) bitmap.Dispose();
            bitmaps.Clear();
        };
    }

    private async Task LoadAsync(string path)
    {
        try
        {
            var document = await Task.Run(() => FmlLayoutReader.Read(path, cancellation.Token), cancellation.Token);
            if (closed) return;
            Document = document;
            canvas = new Canvas { Name = "LayoutCanvas", Width = document.Width, Height = document.Height, ClipToBounds = true };
            var background = document.Components.FirstOrDefault(component => component.Type == "Background");
            canvas.Background = ReadColour(background?.Properties["Values"]?["Colour"]?.GetValue<string>(), Brushes.LightGray);
            var imageFailures = 0;
            foreach (var component in document.Components)
            {
                if (component.Width == 0 || component.Height == 0) continue;
                Control? visual = null;
                if (component.Type is "Background" or "Bitmap")
                {
                    var image = component.Images.FirstOrDefault(item => item.Key == "background_image") ?? component.Images.FirstOrDefault();
                    if (image is not null)
                    {
                        try
                        {
                            using var imageStream = new MemoryStream(image.Bytes, writable: false);
                            var bitmap = new Bitmap(imageStream);
                            bitmaps.Add(bitmap); visual = new Image { Source = bitmap, Stretch = Stretch.Fill };
                        }
                        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
                        { imageFailures++; }
                    }
                }
                else if (component.Type == "Label")
                {
                    var values = component.Properties["Values"];
                    var text = values?["Label (UTF-16)"]?.GetValue<string>() ?? values?["Caption"]?.GetValue<string>() ?? values?["Label"]?.GetValue<string>() ?? "";
                    var font = component.Properties["Fonts"]?["PrimaryFont"];
                    visual = new TextBlock { Name = "LayoutLabel" + component.Index, Text = text,
                        FontFamily = new FontFamily(font?["FontName"]?.GetValue<string>() ?? "Tahoma"),
                        FontSize = Math.Clamp((font?["FontSize"]?.GetValue<double>() ?? 8) * 4 / 3, 6, 144),
                        Foreground = ReadColour(font?["TextColour"]?.GetValue<string>(), Brushes.Black),
                        Background = ReadColour(values?["BackgroundColour"]?.GetValue<string>(), Brushes.Transparent),
                        TextWrapping = TextWrapping.Wrap };
                }
                if (visual is not null) Place(visual, component);
                if (component.Type == "Background") continue;
                var outline = new Border { Name = "ComponentOutline" + component.Index, BorderBrush = Brushes.DeepSkyBlue, BorderThickness = new Thickness(1), Background = Brushes.Transparent };
                ToolTip.SetTip(outline, $"#{component.Index} {component.Type} · number {component.Number}");
                outline.PointerPressed += (_, _) => components.SelectedItem = component;
                outlines[component.Index] = outline; Place(outline, component);
            }
            components.ItemsSource = document.Components;
            SetZoom(); UpdateOutlines();
            var offCanvas = document.Components.Count(component => component.X >= document.Width || component.Y >= document.Height);
            status.Text = $"{document.Components.Count} components · {document.Width:0} × {document.Height:0} · " +
                "Background/bitmap artwork and labels shown; reels, lamps and displays are outlines." +
                (offCanvas > 0 ? $" {offCanvas} components outside the saved background remain available in the inspector." : "") +
                (imageFailures > 0 ? $" {imageFailures} image(s) could not be displayed." : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        { if (!closed) status.Text = "Unable to preview layout: " + exception.Message; }
    }

    private void Place(Control control, LayoutComponent component)
    {
        control.Width = component.Width; control.Height = component.Height;
        Canvas.SetLeft(control, component.X); Canvas.SetTop(control, component.Y);
        if (component.Angle != 0) control.RenderTransform = new RotateTransform(component.Angle);
        canvas!.Children.Add(control);
    }

    private void SetZoom()
    {
        if (canvas is null) return;
        // Detach the saved surface before reparenting between fit and native-size views.
        if (viewport.Child is Viewbox box) box.Child = null;
        if (viewport.Child is ScrollViewer scroll) scroll.Content = null;
        viewport.Child = zoom.SelectedIndex == 1
            ? new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = canvas }
            : new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
    }

    private void UpdateOutlines()
    {
        foreach (var pair in outlines)
        {
            pair.Value.IsVisible = showBounds.IsChecked == true;
            pair.Value.BorderBrush = components.SelectedItem is LayoutComponent selected && selected.Index == pair.Key ? Brushes.Yellow : Brushes.DeepSkyBlue;
            pair.Value.BorderThickness = new Thickness(components.SelectedItem is LayoutComponent current && current.Index == pair.Key ? 2 : 1);
        }
    }

    private static IBrush ReadColour(string? rgba, IBrush fallback)
    {
        if (rgba is null || rgba.Length != 9) return fallback;
        return Color.TryParse("#" + rgba[7..9] + rgba[1..7], out var color) ? new SolidColorBrush(color) : fallback;
    }
}

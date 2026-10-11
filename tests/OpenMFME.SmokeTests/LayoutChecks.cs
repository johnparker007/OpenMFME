using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using OpenMFME.Desktop.Services;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenMFME.Desktop;
using OpenMFME.Formats;
using OpenMFME.LayoutDecoder;

internal static class LayoutChecks
{
    private static byte[] Resource(string name)
    {
        using var stream = typeof(LayoutChecks).Assembly.GetManifestResourceStream("OpenMFME.SmokeTests.Fixtures.layouts." + name)!;
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }

    private static void Pump(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            if (timer.Elapsed.TotalSeconds > 30) throw new Exception("Layout check timed out");
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }

    public static void Run(Action<bool, string> check, string? renderDirectory, string? sampleGame)
    {
        foreach (var type in new[] { "Background", "Lamp", "Reel", "Label", "Button" })
        {
            var bytes = Resource(type + ".layout.fml");
            var document = FmlLayoutReader.Decode(bytes, type + ".fml");
            var expected = JsonNode.Parse(Resource(type + ".expected.json"))!["Components"]!.AsArray();
            check(document.Components.Count == expected.Count, "Upstream component count changed: " + type);
            foreach (var (actual, wanted) in document.Components.Zip(expected))
            {
                var comparison = (JsonObject)wanted!.DeepClone();
                // Upstream's latest fix adds these decoded defaults; its fixture JSON predates that fix.
                if (actual.Type == "Background") { comparison["Values"]!["OffsetX"] ??= 0; comparison["Values"]!["OffsetY"] ??= 0; }
                check(JsonNode.DeepEquals(actual.Properties, comparison), "Upstream component properties changed: " + type + " #" + actual.Index);
            }
            check(document.Width > 0 && document.Height > 0, "Fixture geometry missing: " + type);
        }
        try { FmlLayoutReader.Decode(new byte[147], "short.fml"); throw new Exception("Truncated FML accepted"); }
        catch (InvalidDataException) { check(true, "Truncated FML rejected"); }
        var invalid = Resource("Background.layout.fml");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(132, 4), uint.MaxValue);
        try { FmlLayoutReader.Decode(invalid, "oversized.fml"); throw new Exception("Oversized decoded chunk accepted"); }
        catch (InvalidDataException) { check(true, "Oversized decoded chunk rejected before decompression"); }
        using (var stopped = new CancellationTokenSource())
        {
            stopped.Cancel();
            try { FmlLayoutReader.Decode(Resource("Background.layout.fml"), "cancelled.fml", stopped.Token); throw new Exception("Cancelled decode completed"); }
            catch (OperationCanceledException) { check(true, "Decode cancellation failed"); }
        }
        var missing = new LayoutPreviewWindow(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".fml"));
        missing.Show(); Dispatcher.UIThread.RunJobs(); Pump(missing.LoadCompletion);
        check(missing.Document is null && missing.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "LayoutPreviewStatus")
            .Text!.Contains("Unable to preview"), "Missing layout error did not reach viewer");
        missing.Close();

        var labelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".fml");
        try
        {
            File.WriteAllBytes(labelPath, Resource("Label.layout.fml"));
            var labelPreview = new LayoutPreviewWindow(labelPath); labelPreview.Show(); Dispatcher.UIThread.RunJobs(); Pump(labelPreview.LoadCompletion);
            check(labelPreview.GetVisualDescendants().OfType<TextBlock>().Any(control => control.Name == "LayoutLabel1" && control.Text == "Label"),
                "Saved label text did not render");
            labelPreview.Close();
        }
        finally { File.Delete(labelPath); }

        if (sampleGame is null) return;
        var game = GameMetadata.Read(sampleGame);
        var layoutPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sampleGame)!, game.Layout));
        var before = SHA256.HashData(File.ReadAllBytes(layoutPath));
        var preview = new LayoutPreviewWindow(layoutPath); preview.Show(); Dispatcher.UIThread.RunJobs(); Pump(preview.LoadCompletion);
        if (preview.Document is not { } decoded)
        {
            var message = preview.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "LayoutPreviewStatus").Text!;
            check(message.Contains("resave a copy in MFME v20.1"), "Sample layout failed for an unexpected reason: " + message);
            preview.Close(); return;
        }
        check(decoded.Components.Any(component => component.Type == "Reel") && decoded.Components.Any(component => component.Type == "Lamp"),
            "Sample layout lost reels or lamps");
        check(preview.GetVisualDescendants().OfType<Image>().Any(), "Sample artwork was not rendered");
        var list = preview.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "LayoutComponents");
        list.SelectedItem = decoded.Components.First(component => component.Type == "Reel"); Dispatcher.UIThread.RunJobs();
        check(preview.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "LayoutComponentDetails").Text!.Contains("BandOffset"),
            "Component inspector does not show recovered reel properties");
        var zoom = preview.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "LayoutZoom");
        zoom.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        check(preview.GetVisualDescendants().OfType<ScrollViewer>().Any(control => control.Content is Canvas), "Native-size view is not scrollable");
        zoom.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        var bounds = preview.GetVisualDescendants().OfType<CheckBox>().Single(control => control.Name == "ShowComponentBounds");
        bounds.IsChecked = false; Dispatcher.UIThread.RunJobs();
        if (renderDirectory is not null)
        {
            using var frame = preview.CaptureRenderedFrame()!;
            using var output = File.Create(Path.Combine(renderDirectory, "FmlLayoutPreview.png"));
            frame.Save(output, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        check(preview.GetVisualDescendants().OfType<Border>().Where(control => control.Name?.StartsWith("ComponentOutline") == true).All(control => !control.IsVisible),
            "Component outlines did not toggle off");
        preview.Close();
        check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(layoutPath))), "Layout preview modified its source");
        var closing = new LayoutPreviewWindow(layoutPath); closing.Show(); var pending = closing.LoadCompletion; closing.Close(); Pump(pending);
        check(!closing.IsVisible && closing.Document is null, "Closed loading viewer updated after cancellation");
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "OpenMFME-layout-route-" + Guid.NewGuid().ToString("N"));
        var settingsFile = Path.Combine(settingsDirectory, "settings.json");
        try
        {
            var settings = new SettingsStore(settingsFile); settings.Save(new AppSettings { GameFolders = [Path.GetDirectoryName(sampleGame)!], IncludeSubfolders = false });
            var main = new MainWindow(settings); main.Show(); Dispatcher.UIThread.RunJobs();
            main.FindControl<Button>("BrowseGames")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var manager = main.GameManager!; Pump(manager.ScanCompletion);
            var games = manager.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "GamesList");
            games.SelectedItem = games.ItemsSource!.Cast<GameMetadata>().Single(item => GameLibraryScanner.PathComparer.Equals(item.FilePath, game.FilePath));
            manager.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "InspectGame").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            check(main.FindControl<Button>("ViewGameLayout")!.IsEnabled, "Selected game did not enable layout preview");
            main.FindControl<Button>("ViewGameLayout")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var routed = main.LayoutPreview!; Pump(routed.LoadCompletion);
            check(routed.Document?.FilePath == layoutPath, "View selected layout resolved the wrong FML path");
            main.Close(); check(!routed.IsVisible, "Closing main window left layout preview open");
        }
        finally { if (File.Exists(settingsFile)) File.Delete(settingsFile); if (Directory.Exists(settingsDirectory)) Directory.Delete(settingsDirectory); }
        Console.WriteLine($"Verified FML: {decoded.Components.Count} components · {decoded.Width} × {decoded.Height}.");
    }
}

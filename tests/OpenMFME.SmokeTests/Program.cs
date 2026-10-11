using System.Text;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenMFME.Desktop;
using OpenMFME.Desktop.Reconstruction;
using OpenMFME.Formats;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
byte[] Fixture(Action<BinaryWriter> properties, byte[]? prefix = null)
{
    using var memory = new MemoryStream();
    using var writer = new BinaryWriter(memory);
    writer.Write("TPF0"u8);
    if (prefix is not null) writer.Write(prefix);
    Short(writer, "TForm"); Short(writer, "TestForm");
    properties(writer); writer.Write((byte)0); writer.Write((byte)0);
    return memory.ToArray();
}
void Reject(byte[] bytes)
{
    try { new BinaryDfmReader(bytes).ReadForm(); }
    catch (InvalidDataException) { checks++; return; }
    throw new Exception("Malformed form was accepted");
}
static void Short(BinaryWriter writer, string value)
{
    var text = Encoding.Latin1.GetBytes(value);
    writer.Write((byte)text.Length); writer.Write(text);
}

var simple = Fixture(writer =>
{
    Short(writer, "Left"); writer.Write((byte)2); writer.Write((sbyte)-7);
    Short(writer, "Caption"); writer.Write((byte)20);
    var utf8 = Encoding.UTF8.GetBytes("Test £"); writer.Write(utf8.Length); writer.Write(utf8);
    Short(writer, "Enabled"); writer.Write((byte)9);
    Short(writer, "Anchors"); writer.Write((byte)11); Short(writer, "akLeft"); Short(writer, "");
    Short(writer, "Items"); writer.Write((byte)14);
    writer.Write((byte)2); writer.Write((sbyte)3); writer.Write((byte)1);
    Short(writer, "Caption"); writer.Write((byte)6); Short(writer, "Entry");
    writer.Write((byte)0); writer.Write((byte)0);
    Short(writer, "Picture.Data"); writer.Write((byte)10); writer.Write(3); writer.Write(new byte[] { 1, 2, 3 });
}, new byte[] { 0xF3, 2, 4 });
var parser = new BinaryDfmReader(simple);
var parsed = parser.ReadForm();
Check(parsed.Number("Left") == -7, "Signed integer decoding failed");
Check(parsed.Text("Caption") == "Test £", "UTF-8 decoding failed");
Check(parsed.Boolean("Enabled"), "Boolean decoding failed");
Check(parsed.Flags == 3 && parsed.ChildPosition == 4, "Inherited component prefix failed");
Check(parsed.Properties["Items"].Value?[0]?["properties"]?["Caption"]?["value"]?.GetValue<string>() == "Entry", "Collection item properties failed");
Check(parser.Position == simple.Length, "Parser did not consume the full fixture");
Reject(simple[..^1]);
Reject(Fixture(writer => { Short(writer, "Nested"); for (var depth = 0; depth < 130; depth++) writer.Write((byte)1); }));
Reject(Fixture(writer => { Short(writer, "Unknown"); writer.Write((byte)255); }));
Reject(Fixture(writer => { Short(writer, "Blob"); writer.Write((byte)10); writer.Write(-1); }));
Reject(Fixture(writer => { Short(writer, "Caption"); writer.Write((byte)18); writer.Write(int.MaxValue); }));

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var catalog = RecoveredFormLibrary.Catalog;
Check(catalog.Forms.Count == 66, "Unexpected recovered form count");
Check(catalog.Forms.Sum(form => form.Root.DescendantsAndSelf().Count()) == 4464, "Unexpected component count");
Check(catalog.Failures.Count == 1, "False-positive signature was not recorded");
Check(catalog.Forms.Select(form => form.Root.InstanceName).Distinct().Count() == 66, "Duplicate recovered forms");
var mainMenu = RecoveredFormLibrary.Get("MainForm").Root.Children.Single(node => node.InstanceName == "MainMenu");
Check(mainMenu.Children.Count == 12, "Original top-level menu structure changed");
Check(mainMenu.DescendantsAndSelf().Any(node => node.Text("Caption") == "ROM Loader"), "ROM Loader menu was not recovered");

// Verify packaged glyphs against the original catalog, without requiring MFME.exe.
var glyphNodes = catalog.Forms.SelectMany(form => form.Root.DescendantsAndSelf())
    .Where(node => node.Properties.ContainsKey("Glyph.Data")).ToArray();
Check(glyphNodes.Length == 41, "Unexpected glyph reference count");
foreach (var node in glyphNodes)
{
    var metadata = node.Properties["Glyph.Data"].Value!;
    var hash = metadata["sha256"]!.GetValue<string>();
    using var stream = AssetLoader.Open(new Uri($"avares://OpenMFME.Desktop/Assets/recovered-images/{hash}.bmp"));
    using var bytes = new MemoryStream(); stream.CopyTo(bytes);
    using var wrapper = new MemoryStream();
    using (var writer = new BinaryWriter(wrapper, Encoding.UTF8, true))
    { writer.Write((int)bytes.Length); writer.Write(bytes.ToArray()); }
    Check(Convert.ToHexString(SHA256.HashData(wrapper.ToArray())).ToLowerInvariant() == hash,
        $"Packaged glyph differs from source: {node.InstanceName}");
    foreach (var disabled in new[] { false, true })
    {
        var glyph = RecoveredGraphics.Glyph(node, disabled) as WriteableBitmap;
        Check(glyph is not null, $"Missing glyph: {node.InstanceName}");
        using var decoded = new Bitmap(new MemoryStream(bytes.ToArray()));
        Check(glyph!.PixelSize.Width * (int)node.Number("NumGlyphs", 1) == decoded.PixelSize.Width &&
            glyph.PixelSize.Height == decoded.PixelSize.Height, "Glyph frame cropping failed");
        using var framebuffer = glyph.Lock();
        Check(Marshal.ReadByte(framebuffer.Address, (glyph.PixelSize.Height - 1) * framebuffer.RowBytes + 3) == 0,
            "Glyph background transparency failed");
    }
}
var configFactory = new RecoveredFormFactory();
var configPreview = configFactory.CreateWindow(RecoveredFormLibrary.Get("Config"));
Check(!configFactory.UnsupportedClasses.Contains("TzcLightOnOff"), "DIP preview still uses a class placeholder");
configPreview.Close();

var renderDirectory = args.Length == 2 && args[0] == "--render-dir" ? Path.GetFullPath(args[1]) : null;
if (renderDirectory is not null) Directory.CreateDirectory(renderDirectory);
var renderForms = new[] { "AlignmentForm", "GeneralConfig", "GameNotes", "Config", "SizeForm", "SnapForm", "GraphicEditorForm", "PlatformForm", "PropertiesForm", "ReelEditorForm" };
foreach (var form in catalog.Forms)
{
    var factory = new RecoveredFormFactory();
    var window = factory.CreateWindow(form);
    window.Show();
    Dispatcher.UIThread.RunJobs();
    Check(window.IsVisible && window.Bounds.Width > 0, $"Preview failed: {form.Root.InstanceName}");
    using var frame = window.CaptureRenderedFrame() ?? throw new Exception($"No preview frame: {form.Root.InstanceName}");
    Check(frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0, $"Empty preview frame: {form.Root.InstanceName}");
    if (renderDirectory is not null && renderForms.Contains(form.Root.InstanceName))
    {
        using (var destination = File.Create(Path.Combine(renderDirectory, form.Root.InstanceName + ".png"))) frame.Save(destination, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    if (form.Root.InstanceName == "AlignmentForm")
    {
        var cancel = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "Cancel");
        var apply = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "Apply");
        Check(cancel.IsEnabled && !apply.IsEnabled, "Preview commands imply unsupported behavior");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!window.IsVisible, "Cancel did not close the preview");
    }
    window.Close();
}

var mainWindow = new MainWindow();
mainWindow.Show(); Dispatcher.UIThread.RunJobs();
Check(mainWindow.IsVisible, "Main shell failed to open");
var menu = mainWindow.FindControl<Menu>("MainMenuBar")!;
Check(menu.ItemsSource!.Cast<object>().Count() == 13, "Recovered main menu plus reconstruction menu missing");
if (renderDirectory is not null)
{
    using var frame = mainWindow.CaptureRenderedFrame() ?? throw new Exception("No main frame");
    using (var destination = File.Create(Path.Combine(renderDirectory, "OpenMFME.png"))) frame.Save(destination, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
}
mainWindow.Close();
var browser = new FormBrowserWindow();
browser.Show(); Dispatcher.UIThread.RunJobs();
Check(browser.IsVisible, "Form browser failed to open");
browser.Close();
Console.WriteLine($"Passed {checks} checks, including rendering all 66 form previews.");

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenMFME.Desktop.Reconstruction;
using OpenMFME.Formats;

internal static class FidelityChecks
{
    private static DfmComponent Node(string type, string name, params (string Key, object Value)[] properties) => new()
    {
        ClassName = type, InstanceName = name,
        Properties = properties.ToDictionary(pair => pair.Key, pair => new DfmValue("Fixture", JsonSerializer.SerializeToNode(pair.Value)))
    };

    public static void Run(Action<bool, string> check)
    {
        var panel = new RecoveredLayoutPanel(300, 200);
        Border Add(string name, params (string Key, object Value)[] properties)
        {
            var node = Node("TPanel", name, properties);
            var control = new Border { Width = node.Number("Width", 40), Height = node.Number("Height", 20) };
            panel.Add(control, node);
            return control;
        }
        var client = Add("Client", ("Align", "alClient"));
        var top = Add("Top", ("Align", "alTop"), ("Height", 30));
        var bottom = Add("Bottom", ("Align", "alBottom"), ("Top", 180), ("Height", 20));
        var left = Add("Left", ("Align", "alLeft"), ("Width", 40));
        var right = Add("Right", ("Align", "alRight"), ("Width", 50));
        var hidden = Add("Hidden", ("Align", "alTop"), ("Height", 100));
        hidden.IsVisible = false;
        var anchored = Add("Anchored", ("Left", 200), ("Top", 170), ("Width", 50), ("Height", 20),
            ("Anchors", new[] { "akRight", "akBottom" }));
        var stretched = Add("Stretched", ("Left", 10), ("Top", 10), ("Width", 100), ("Height", 40),
            ("Anchors", new[] { "akLeft", "akTop", "akRight", "akBottom" }));
        var centered = Add("Centered", ("Left", 100), ("Top", 80), ("Width", 50), ("Height", 20),
            ("Anchors", Array.Empty<string>()));
        panel.Measure(new Size(300, 200)); panel.Arrange(new Rect(0, 0, 300, 200));
        check(top.Bounds == new Rect(0, 0, 300, 30), "Top docking failed");
        check(bottom.Bounds == new Rect(0, 180, 300, 20), "Bottom docking failed");
        check(left.Bounds == new Rect(0, 30, 40, 150), "Left docking failed");
        check(right.Bounds == new Rect(250, 30, 50, 150), "Right docking failed");
        check(client.Bounds == new Rect(40, 30, 210, 150), "Client docking or hidden-control exclusion failed");
        panel.Measure(new Size(400, 280)); panel.Arrange(new Rect(0, 0, 400, 280));
        check(anchored.Bounds == new Rect(300, 250, 50, 20), "Right/bottom anchors failed");
        check(stretched.Bounds == new Rect(10, 10, 200, 120), "Opposite-edge stretching failed");
        check(centered.Bounds == new Rect(150, 120, 50, 20), "Unanchored centering failed");
        panel.Measure(new Size(300, 200)); panel.Arrange(new Rect(0, 0, 300, 200));
        check(anchored.Bounds == new Rect(200, 170, 50, 20), "Repeated resize accumulated anchor drift");

        var root = Node("TForm", "FontFixture", ("ClientWidth", 420), ("ClientHeight", 220),
            ("Font.Name", "Arial"), ("Font.Height", -15), ("Font.Style", new[] { "fsBold" }), ("Font.Color", "clBlue"));
        var group = Node("TPanel", "Container", ("Width", 420), ("Height", 220));
        var first = Node("TEdit", "First", ("Left", 10), ("Top", 10), ("Width", 120), ("Height", 24), ("TabOrder", 0));
        var second = Node("TEdit", "Second", ("Left", 10), ("Top", 40), ("Width", 120), ("Height", 24), ("TabOrder", 1));
        var own = Node("TEdit", "Own", ("Left", 10), ("Top", 70), ("Width", 120), ("Height", 24),
            ("ParentFont", false), ("Font.Name", "Courier New"), ("Font.Height", -11), ("TabStop", false));
        // Deliberately reverse the first two children: TabOrder must take precedence over tree order.
        group.Children.AddRange([second, first, own]); root.Children.Add(group);
        var preview = new RecoveredFormFactory().CreateWindow(new RecoveredForm("fixture", 0, root));
        preview.Show(); Dispatcher.UIThread.RunJobs();
        var edits = preview.GetVisualDescendants().OfType<TextBox>().ToDictionary(control => control.Name!);
        check(edits["First"].FontSize == 15 && edits["First"].FontFamily.Name == "Arial" &&
            edits["First"].FontWeight == FontWeight.Bold, "Nested parent-font inheritance failed");
        check(edits["First"].Foreground is ISolidColorBrush brush && brush.Color == Colors.Blue,
            "Inherited font colour failed");
        check(edits["Own"].FontSize == 11 && edits["Own"].FontFamily.Name == "Courier New" &&
            edits["Own"].FontWeight == FontWeight.Normal, "ParentFont=false failed");
        check(!edits["Own"].Focusable, "TabStop=false was ignored");
        edits["First"].Focus();
        preview.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); preview.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        check(edits["Second"].IsFocused, "Tab navigation ignored recovered TabOrder");
        preview.Close();

        var graphics = new RecoveredFormFactory().CreateWindow(RecoveredFormLibrary.Get("GraphicEditorForm"));
        graphics.Show(); Dispatcher.UIThread.RunJobs();
        var controls = graphics.GetVisualDescendants().OfType<Control>().Where(control => control.Name is not null).ToArray();
        var mainPanel = controls.Single(control => control.Name == "Panel1");
        var lowerPanel = controls.Single(control => control.Name == "Panel2");
        check(Math.Abs(mainPanel.Bounds.Width - 632) < 1 && Math.Abs(mainPanel.Bounds.Height - 291) < 1 &&
            Math.Abs(lowerPanel.Bounds.Height - 329) < 1, "Initial Graphic Editor size differs from its saved geometry");
        var oldMain = mainPanel.Bounds; var oldLower = lowerPanel.Bounds;
        graphics.Width += 100; graphics.Height += 80; Dispatcher.UIThread.RunJobs();
        check(Math.Abs(mainPanel.Bounds.Width - oldMain.Width - 100) < 1 &&
            Math.Abs(mainPanel.Bounds.Height - oldMain.Height - 80) < 1, "Real graphic-editor canvas did not grow");
        check(Math.Abs(lowerPanel.Bounds.Y - oldLower.Y - 80) < 1 &&
            lowerPanel.Bounds.Height == oldLower.Height, "Graphic-editor bottom toolbar lost its docking");
        var brightness = controls.OfType<ScrollBar>().Single(control => control.Name == "Brightness");
        var increase = brightness.GetVisualDescendants().OfType<RepeatButton>().Single(control => control.Name == "PART_LineDownButton");
        var previous = brightness.Value;
        increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(brightness.Value == previous + brightness.SmallChange, "Compact scrollbar arrow is not connected");
        check(brightness.GetVisualDescendants().OfType<Thumb>().Single().Bounds.Height <= brightness.Bounds.Height,
            "Compact scrollbar thumb exceeds its saved height");
        brightness.Focus(); previous = brightness.Value;
        graphics.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        graphics.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        check(brightness.Value == previous + brightness.SmallChange, $"Compact scrollbar lost keyboard focus/input: focused={brightness.IsFocused}, focusable={brightness.Focusable}, old={previous}, value={brightness.Value}, step={brightness.SmallChange}");
        var scrollThumb = brightness.GetVisualDescendants().OfType<Thumb>().Single();
        var scrollPoint = scrollThumb.TranslatePoint(new Point(scrollThumb.Bounds.Width / 2, scrollThumb.Bounds.Height / 2), graphics)!.Value;
        previous = brightness.Value;
        graphics.MouseDown(scrollPoint, MouseButton.Left, RawInputModifiers.None);
        graphics.MouseMove(scrollPoint + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
        graphics.MouseUp(scrollPoint + new Vector(20, 0), MouseButton.Left, RawInputModifiers.None);
        check(brightness.Value > previous, "Compact scrollbar thumb drag did not change its value");
        graphics.Close();

        var preferences = new RecoveredFormFactory().CreateWindow(RecoveredFormLibrary.Get("GeneralConfig"));
        preferences.Show(); Dispatcher.UIThread.RunJobs();
        check(!preferences.CanResize, "Saved fixed preferences window became resizable");
        var slider = preferences.GetVisualDescendants().OfType<Slider>().Single();
        slider.Value = (slider.Minimum + slider.Maximum) / 2;
        var oldValue = slider.Value;
        slider.Focus();
        preferences.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); preferences.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        check(slider.Value > oldValue, "Compact slider lost keyboard interaction");
        check(slider.GetVisualDescendants().OfType<Thumb>().Single().Bounds.Height <= slider.Bounds.Height,
            "Compact slider thumb is clipped");
        var sliderThumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        var sliderPoint = sliderThumb.TranslatePoint(new Point(sliderThumb.Bounds.Width / 2, sliderThumb.Bounds.Height / 2), preferences)!.Value;
        oldValue = slider.Value;
        preferences.MouseDown(sliderPoint, MouseButton.Left, RawInputModifiers.None);
        preferences.MouseMove(sliderPoint + new Vector(15, 0), RawInputModifiers.LeftMouseButton);
        preferences.MouseUp(sliderPoint + new Vector(15, 0), MouseButton.Left, RawInputModifiers.None);
        check(slider.Value > oldValue, "Compact slider thumb drag did not change its value");
        preferences.Close();
    }
}

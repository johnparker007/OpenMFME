using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpenMFME.Desktop.Reconstruction;

namespace OpenMFME.Desktop;

internal static class GameManagerDialogs
{
    public static (Window Window, RecoveredLayoutPanel Surface, TextBlock Message) Create(string formName)
    {
        var root = RecoveredFormLibrary.Get(formName).Root;
        var surface = new RecoveredFormFactory().CreateSurface(root);
        var message = new TextBlock { Name = "DialogMessage", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), FontSize = 11 };
        var dock = new DockPanel();
        DockPanel.SetDock(message, Dock.Bottom); dock.Children.Add(message); dock.Children.Add(surface);
        var window = new Window
        {
            Title = root.Text("Caption"), Width = root.Number("ClientWidth"),
            Height = root.Number("ClientHeight") + 48, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock
        };
        RecoveredFormFactory.AddCompactStyles(window);
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } };
        return (window, surface, message);
    }

    public static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
}

internal sealed class GameManagerFind
{
    public Window Window { get; }
    public GameManagerFind(Func<string, bool> find)
    {
        var dialog = GameManagerDialogs.Create("ManagerFindForm"); Window = dialog.Window;
        var name = GameManagerDialogs.Named<TextBox>(dialog.Surface, "Name");
        var button = GameManagerDialogs.Named<Button>(dialog.Surface, "Find");
        button.IsDefault = true; ToolTip.SetTip(button, null);
        name.TextChanged += (_, _) => button.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
        button.Click += (_, _) =>
            dialog.Message.Text = find(name.Text!.Trim()) ? "" : "No matching name in the current game list.";
        Window.Opened += (_, _) => name.Focus();
    }
}

internal sealed class GameManagerFilter
{
    public Window Window { get; }
    private readonly StackPanel platforms = new() { Spacing = 3 };
    private readonly HashSet<string> excluded;
    private readonly Action<string> nameChanged;
    private readonly Action changed;
    private readonly TextBox name;

    public GameManagerFilter(string currentName, HashSet<string> excluded, Action<string> nameChanged, Action changed, Action refresh)
    {
        this.excluded = excluded; this.nameChanged = nameChanged; this.changed = changed;
        var dialog = GameManagerDialogs.Create("FilterForm"); Window = dialog.Window;
        name = GameManagerDialogs.Named<TextBox>(dialog.Surface, "FilterName"); name.Text = currentName;
        name.TextChanged += (_, _) => this.nameChanged(name.Text?.Trim() ?? "");
        foreach (var controlName in new[] { "FilterTags", "ShowWIPs", "ShowNonRunners", "DXs", "NotDXs", "LoTechs", "NotLoTechs", "NotMultiPlayers", "MultiPlayers" })
        {
            var control = GameManagerDialogs.Named<Control>(dialog.Surface, controlName);
            control.IsEnabled = false;
            ToolTip.SetTip(control, "This information is not available in the game library yet.");
        }
        dialog.Message.Text = "Name and platform filters apply immediately. Tags and layout categories are not available yet.";
        foreach (var buttonName in new[] { "ClearAllButton", "SelectAllButton", "ResetButton", "RefreshButton" })
        {
            var button = GameManagerDialogs.Named<Button>(dialog.Surface, buttonName);
            button.IsEnabled = true; ToolTip.SetTip(button, null);
        }
        var group = GameManagerDialogs.Named<Grid>(dialog.Surface, "IncludedTechs");
        var overlay = new Canvas { IsHitTestVisible = true };
        var scroll = new ScrollViewer { Width = 300, Height = 355, Content = platforms };
        Canvas.SetLeft(scroll, 8); Canvas.SetTop(scroll, 54); overlay.Children.Add(scroll); group.Children.Add(overlay);
        GameManagerDialogs.Named<Button>(dialog.Surface, "ClearAllButton").Click += (_, _) => SetAll(false);
        GameManagerDialogs.Named<Button>(dialog.Surface, "SelectAllButton").Click += (_, _) => SetAll(true);
        GameManagerDialogs.Named<Button>(dialog.Surface, "ResetButton").Click += (_, _) =>
        { name.Text = ""; excluded.Clear(); UpdateChecks(); this.changed(); };
        GameManagerDialogs.Named<Button>(dialog.Surface, "RefreshButton").Click += (_, _) => refresh();
    }

    public void UpdatePlatforms(IEnumerable<string> systems)
    {
        platforms.Children.Clear();
        foreach (var system in systems.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            var check = new CheckBox { Content = system.Length == 0 ? "Unspecified platform" : system, Tag = system, IsChecked = !excluded.Contains(system) };
            check.IsCheckedChanged += (_, _) =>
            { if (check.IsChecked == true) excluded.Remove(system); else excluded.Add(system); changed(); };
            platforms.Children.Add(check);
        }
    }

    private void SetAll(bool selected)
    {
        foreach (var check in platforms.Children.OfType<CheckBox>())
        { var system = (string)check.Tag!; if (selected) excluded.Remove(system); else excluded.Add(system); }
        UpdateChecks(); changed();
    }

    private void UpdateChecks()
    {
        foreach (var check in platforms.Children.OfType<CheckBox>()) check.IsChecked = !excluded.Contains((string)check.Tag!);
    }
}

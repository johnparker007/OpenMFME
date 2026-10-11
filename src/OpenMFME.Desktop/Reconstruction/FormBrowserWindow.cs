using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OpenMFME.Formats;

namespace OpenMFME.Desktop.Reconstruction;

public sealed class FormBrowserWindow : Window
{
    private readonly ListBox list = new();
    private readonly TextBox search = new() { PlaceholderText = "Find a window or dialog…" };
    private readonly TextBlock details = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button open = new() { Content = "Open UI preview", IsEnabled = false };

    public FormBrowserWindow()
    {
        Title = "MFME form browser";
        Width = 760; Height = 620; MinWidth = 560; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 14) };
        top.Children.Add(new TextBlock { Text = "Recovered MFME windows", FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        top.Children.Add(new TextBlock
        {
            Text = "Browse reconstructed controls and menus. Custom graphics are placeholders; changes are temporary.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        top.Children.Add(search);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        var footer = new StackPanel { Spacing = 10, Margin = new Thickness(0, 14, 0, 0) };
        footer.Children.Add(details); footer.Children.Add(open);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(list);
        Content = panel;
        search.TextChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) =>
        {
            open.IsEnabled = list.SelectedItem is Entry;
            if (list.SelectedItem is Entry entry)
                details.Text = $"{entry.Form.Root.InstanceName} · {entry.Form.Root.DescendantsAndSelf().Count()} components · {entry.Form.ByteLength:N0} form-stream bytes";
            else
                details.Text = "";
        };
        open.Click += (_, _) =>
        {
            if (list.SelectedItem is Entry entry)
                new RecoveredFormFactory().CreateWindow(entry.Form).Show(this);
        };
        Refresh();
    }

    private void Refresh()
    {
        var query = search.Text ?? "";
        list.ItemsSource = RecoveredFormLibrary.Catalog.Forms
            .Where(form => (form.Root.InstanceName + " " + form.Root.Text("Caption")).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(form => form.Root.Text("Caption", form.Root.InstanceName), StringComparer.OrdinalIgnoreCase)
            .Select(form => new Entry(form)).ToArray();
    }

    private sealed record Entry(RecoveredForm Form)
    {
        public override string ToString()
        {
            var title = Form.Root.Text("Caption", Form.Root.InstanceName);
            return $"{title}  ·  {Form.Root.InstanceName}";
        }
    }
}

using Avalonia.Controls;
using OpenMFME.Formats;

namespace OpenMFME.Desktop.Reconstruction;

public static class RecoveredMenuFactory
{
    public static Control CreateItem(DfmComponent node, Func<DfmComponent, Action?> resolve)
    {
        var caption = node.Text("Caption");
        if (caption == "-")
            return new Separator();
        var children = node.Children.Select(child => CreateItem(child, resolve)).ToArray();
        var action = resolve(node);
        var item = new MenuItem
        {
            Name = node.InstanceName,
            Header = caption.Replace("&", "_"),
            ItemsSource = children,
            IsVisible = node.Boolean("Visible", true),
            IsEnabled = children.Length > 0 || action is not null
        };
        if (action is not null)
            item.Click += (_, e) =>
            {
                if (ReferenceEquals(e.Source, item))
                    action();
            };
        else if (children.Length == 0)
            ToolTip.SetTip(item, "This command has not been implemented.");
        return item;
    }
}

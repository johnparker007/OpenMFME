using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using OpenMFME.Formats;

namespace OpenMFME.Desktop.Reconstruction;

/// <summary>Saved bounds plus common VCL docking/anchors, without scaling the original coordinates.</summary>
public sealed class RecoveredLayoutPanel : Panel
{
    private sealed record Item(Control Control, DfmComponent Node, bool AutoWidth);
    private readonly List<Item> items = [];
    public Size DesignSize { get; }
    public Thickness ClientInset { get; set; }
    public RecoveredLayoutPanel(double width, double height)
    {
        DesignSize = new Size(Math.Max(1, width), Math.Max(1, height));
        ClipToBounds = true;
        Avalonia.Input.KeyboardNavigation.SetTabNavigation(this, Avalonia.Input.KeyboardNavigationMode.Local);
    }

    public void Add(Control control, DfmComponent node)
    {
        var autoWidth = control is TextBlock && node.ClassName is "TLabel" or "TStaticText" && node.Boolean("AutoSize", true);
        var anchors = Anchors(node);
        var align = Align(node);
        if (autoWidth || align is "alTop" or "alBottom" or "alClient" || anchors.Contains("akLeft") && anchors.Contains("akRight"))
            control.Width = double.NaN;
        if (align is "alLeft" or "alRight" or "alClient" || anchors.Contains("akTop") && anchors.Contains("akBottom"))
            control.Height = double.NaN;
        items.Add(new Item(control, node, autoWidth));
        Children.Add(control);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : DesignSize.Width,
            double.IsFinite(availableSize.Height) ? availableSize.Height : DesignSize.Height);
        foreach (var (item, bounds) in BoundsFor(size))
            item.Control.Measure(new Size(item.AutoWidth ? double.PositiveInfinity : bounds.Width, bounds.Height));
        return DesignSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (item, bounds) in BoundsFor(finalSize))
        {
            var target = item.AutoWidth ? new Rect(bounds.Position, new Size(item.Control.DesiredSize.Width, bounds.Height)) : bounds;
            item.Control.Arrange(target);
        }
        return finalSize;
    }

    private IEnumerable<(Item Item, Rect Bounds)> BoundsFor(Size size)
    {
        var remaining = new Rect(ClientInset.Left, ClientInset.Top,
            Math.Max(0, size.Width - ClientInset.Left - ClientInset.Right),
            Math.Max(0, size.Height - ClientInset.Top - ClientInset.Bottom));
        // Group by docking edge; source coordinates determine order within an edge.
        foreach (var edge in new[] { "alTop", "alBottom", "alLeft", "alRight", "alClient" })
        {
            var ordered = items.Where(item => item.Control.IsVisible && Align(item.Node) == edge)
                .OrderBy(item => edge switch
                {
                    "alBottom" => -item.Node.Number("Top"),
                    "alRight" => -item.Node.Number("Left"),
                    "alTop" => item.Node.Number("Top"),
                    _ => item.Node.Number("Left")
                });
            foreach (var item in ordered)
            {
                var width = Math.Min(remaining.Width, Math.Max(0, item.Node.Number("Width", item.Control.DesiredSize.Width)));
                var height = Math.Min(remaining.Height, Math.Max(0, item.Node.Number("Height", item.Control.DesiredSize.Height)));
                Rect bounds;
                switch (edge)
                {
                    case "alTop":
                        bounds = new Rect(remaining.X, remaining.Y, remaining.Width, height);
                        remaining = new Rect(remaining.X, remaining.Y + height, remaining.Width, remaining.Height - height);
                        break;
                    case "alBottom":
                        bounds = new Rect(remaining.X, remaining.Bottom - height, remaining.Width, height);
                        remaining = new Rect(remaining.X, remaining.Y, remaining.Width, remaining.Height - height);
                        break;
                    case "alLeft":
                        bounds = new Rect(remaining.X, remaining.Y, width, remaining.Height);
                        remaining = new Rect(remaining.X + width, remaining.Y, remaining.Width - width, remaining.Height);
                        break;
                    case "alRight":
                        bounds = new Rect(remaining.Right - width, remaining.Y, width, remaining.Height);
                        remaining = new Rect(remaining.X, remaining.Y, remaining.Width - width, remaining.Height);
                        break;
                    default: bounds = remaining; break;
                }
                yield return (item, bounds);
            }
        }
        foreach (var item in items.Where(item => item.Control.IsVisible && Align(item.Node) is not ("alTop" or "alBottom" or "alLeft" or "alRight" or "alClient")))
        {
            var node = item.Node;
            var anchors = Anchors(node);
            var x = node.Number("Left"); var y = node.Number("Top");
            var width = node.Number("Width", item.Control.DesiredSize.Width);
            var height = node.Number("Height", item.Control.DesiredSize.Height);
            var dx = size.Width - DesignSize.Width; var dy = size.Height - DesignSize.Height;
            if (anchors.Contains("akLeft") && anchors.Contains("akRight")) width += dx;
            else if (anchors.Contains("akRight")) x += dx;
            else if (!anchors.Contains("akLeft")) x += dx / 2;
            if (anchors.Contains("akTop") && anchors.Contains("akBottom")) height += dy;
            else if (anchors.Contains("akBottom")) y += dy;
            else if (!anchors.Contains("akTop")) y += dy / 2;
            yield return (item, new Rect(x, y, Math.Max(0, width), Math.Max(0, height)));
        }
    }

    private static string Align(DfmComponent node) => node.Text("Align", node.ClassName == "TStatusBar" ? "alBottom" : "alNone");
    private static HashSet<string> Anchors(DfmComponent node) =>
        node.Properties.TryGetValue("Anchors", out var value) && value.Value is JsonArray set
            ? set.Select(entry => entry!.GetValue<string>()).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(["akLeft", "akTop"], StringComparer.Ordinal);
}

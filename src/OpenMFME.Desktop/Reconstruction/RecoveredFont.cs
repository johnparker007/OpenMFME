using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using OpenMFME.Formats;

namespace OpenMFME.Desktop.Reconstruction;

internal sealed record RecoveredFont(string Name, double Size, FontWeight Weight, FontStyle Style, IBrush Foreground)
{
    public static RecoveredFont Default { get; } = new("Tahoma", 11, FontWeight.Normal, FontStyle.Normal, Brushes.Black);
    public static RecoveredFont Resolve(DfmComponent node, RecoveredFont parent)
    {
        var inherits = node.Boolean("ParentFont", true);
        var basis = inherits ? parent : Default;
        var name = node.Text("Font.Name", basis.Name);
        if (name == "MS Sans Serif") name = "Microsoft Sans Serif";
        var size = node.Properties.ContainsKey("Font.Height") ? Math.Abs(node.Number("Font.Height")) : basis.Size;
        var weight = basis.Weight; var style = basis.Style;
        if (node.Properties.TryGetValue("Font.Style", out var value) && value.Value is JsonArray styles)
        {
            weight = styles.Any(entry => entry?.GetValue<string>() == "fsBold") ? FontWeight.Bold : FontWeight.Normal;
            style = styles.Any(entry => entry?.GetValue<string>() == "fsItalic") ? FontStyle.Italic : FontStyle.Normal;
        }
        return new RecoveredFont(name, Math.Max(1, size), weight, style,
            RecoveredFormFactory.Colour(node, "Font.Color", basis.Foreground));
    }
    public void Apply(Control control)
    {
        control.SetValue(TextElement.FontFamilyProperty, new FontFamily(Name));
        control.SetValue(TextElement.FontSizeProperty, Size);
        control.SetValue(TextElement.FontWeightProperty, Weight);
        control.SetValue(TextElement.FontStyleProperty, Style);
        control.SetValue(TextElement.ForegroundProperty, Foreground);
    }
}

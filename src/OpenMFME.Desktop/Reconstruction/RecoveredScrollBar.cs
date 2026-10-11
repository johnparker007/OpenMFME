using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

namespace OpenMFME.Desktop.Reconstruction;

/// <summary>Standalone saved scrollbars need keyboard input in addition to Avalonia's scroll-viewer usage.</summary>
public sealed class RecoveredScrollBar : ScrollBar
{
    protected override Type StyleKeyOverride => typeof(ScrollBar);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        double? value = e.Key switch
        {
            Key.Home => Minimum,
            Key.End => Maximum,
            Key.PageUp => Value - LargeChange,
            Key.PageDown => Value + LargeChange,
            Key.Left when Orientation == Orientation.Horizontal => Value - SmallChange,
            Key.Right when Orientation == Orientation.Horizontal => Value + SmallChange,
            Key.Up when Orientation == Orientation.Vertical => Value - SmallChange,
            Key.Down when Orientation == Orientation.Vertical => Value + SmallChange,
            _ => null
        };
        if (value is not null)
        {
            SetCurrentValue(ValueProperty, Math.Clamp(value.Value, Minimum, Maximum));
            e.Handled = true;
        }
    }
}

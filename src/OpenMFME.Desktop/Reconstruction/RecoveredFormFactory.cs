using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.Styling;
using OpenMFME.Formats;

namespace OpenMFME.Desktop.Reconstruction;

/// <summary>First-pass Avalonia views from the neutral recovered-form model. Original handlers are never executed.</summary>
public sealed class RecoveredFormFactory
{
    public HashSet<string> UnsupportedClasses { get; } = new(StringComparer.Ordinal);
    private Window? window;

    public Window CreateWindow(RecoveredForm form)
    {
        var root = form.Root;
        var width = root.Number("ClientWidth", 640);
        var height = root.Number("ClientHeight", 480);
        window = new Window
        {
            Title = $"{root.Text("Caption", root.InstanceName)} — UI preview",
            Width = Math.Clamp(width + 24, 320, 1400),
            Height = Math.Clamp(height + 112, 240, 950),
            MinWidth = 320,
            MinHeight = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = CreateContent(root, width, height)
        };
        AddCompactStyles(window);
        return window;
    }

    private Control CreateContent(DfmComponent root, double width, double height)
    {
        var surface = CreateSurface(root, width, height);
        var dock = new DockPanel { Background = Background(root) };
        var note = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFF4D6")),
            Padding = new Thickness(12, 8),
            Child = new TextBlock
            {
                Text = "UI preview · Changes are temporary; original commands are not implemented.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap
            }
        };
        DockPanel.SetDock(note, Dock.Top);
        dock.Children.Add(note);
        var unsupported = new TextBlock
        {
            Text = UnsupportedClasses.Count == 0 ? "Standard controls reconstructed from the saved form."
                : $"Placeholders: {string.Join(", ", UnsupportedClasses.Order())}",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 6)
        };
        DockPanel.SetDock(unsupported, Dock.Bottom);
        dock.Children.Add(unsupported);
        var mainMenu = root.Children.FirstOrDefault(child => child.ClassName == "TMainMenu");
        if (mainMenu is not null)
        {
            var menu = new Menu { ItemsSource = mainMenu.Children.Select(node => RecoveredMenuFactory.CreateItem(node, _ => null)).ToArray() };
            DockPanel.SetDock(menu, Dock.Top);
            dock.Children.Add(menu);
        }
        dock.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = surface
        });
        return dock;
    }

    public Canvas CreateSurface(DfmComponent node, double? width = null, double? height = null)
    {
        var canvas = new Canvas
        {
            Width = width ?? node.Number("Width", node.Number("ClientWidth", 640)),
            Height = height ?? node.Number("Height", node.Number("ClientHeight", 480)),
            Background = Background(node),
            ClipToBounds = true
        };
        foreach (var child in node.Children)
        {
            var control = CreateControl(child);
            if (control is null)
                continue;
            Canvas.SetLeft(control, child.Number("Left"));
            Canvas.SetTop(control, child.Number("Top"));
            canvas.Children.Add(control);
        }
        return canvas;
    }

    private Control? CreateControl(DfmComponent node)
    {
        Control? control = node.ClassName switch
        {
            "TLabel" or "TStaticText" or "TzcURLLabel" or "Tz3DLabel" => Label(node),
            "TEdit" => Edit(node),
            "TLabeledEdit" => LabeledEdit(node),
            "TMemo" or "TRichEdit" => Memo(node),
            "TButton" or "TSpeedButton" or "TBitBtn" => Button(node),
            "TCheckBox" => new CheckBox { Content = Caption(node), IsChecked = node.Boolean("Checked") },
            "TRadioButton" => new RadioButton { Content = Caption(node), IsChecked = node.Boolean("Checked") },
            "TComboBox" => Combo(node),
            "TListBox" => new ListBox { ItemsSource = Strings(node, "Items.Strings") },
            "TPanel" or "TCustomCaptionPanel" => Panel(node),
            "TGroupBox" => Group(node, CreateSurface(node)),
            "TRadioGroup" => RadioGroup(node),
            "TPageControl" or "TPageControl1" => Pages(node),
            "TTabControl" or "TTabSet" => Tabs(node),
            "TTabSheet" or "TTabSheet1" => CreateSurface(node),
            "TScrollBox" => new ScrollViewer { Content = CreateSurface(node) },
            "TTrackBar" or "TScrollBar" => Slider(node),
            "TProgressBar" => new ProgressBar { Minimum = node.Number("Min"), Maximum = node.Number("Max", 100), Value = node.Number("Position") },
            "TShape" or "TBevel" => Shape(node),
            "TSplitter" => new Border { Background = Brushes.LightGray },
            "TUpDown" => UpDown(node),
            "TStatusBar" => new TextBlock { Text = node.Text("SimpleText") },
            // These components represent services, menus or dialogs rather than visible controls.
            "TMainMenu" or "TMenuItem" or "TPopupMenu" or "TTimer" or "TThreadedTimer" or
            "TOpenDialog" or "TSaveDialog" or "TOpenPictureDialog" or "TSavePictureDialog" or
            "TColorDialog" or "TFontDialog" or "TPrintDialog" or "TImageList" or "TBitmap32List" or
            "TApplicationEvents" or "TNetHTTPClient" or "TNetHTTPRequest" => null,
            _ => Placeholder(node)
        };
        if (control is null)
            return null;
        if (!string.IsNullOrEmpty(node.InstanceName))
            control.Name = node.InstanceName;
        if (node.Properties.ContainsKey("Width") && !(control is TextBlock && node.ClassName is "TLabel" or "TStaticText" && node.Boolean("AutoSize", true)))
            control.Width = Math.Max(1, node.Number("Width"));
        if (node.Properties.ContainsKey("Height"))
            control.Height = Math.Max(1, node.Number("Height"));
        control.IsVisible = node.Boolean("Visible", true);
        control.IsEnabled &= node.Boolean("Enabled", true);
        control.MinWidth = control.MinHeight = 0;
        if (control is TemplatedControl templated)
        {
            templated.FontSize = Math.Max(9, Math.Abs(node.Number("Font.Height", -11)));
            templated.FontFamily = new FontFamily(FontName(node));
        }
        if (node.Text("Hint") is { Length: > 0 } hint)
            ToolTip.SetTip(control, hint);
        return control;
    }

    private static TextBlock Label(DfmComponent node) => new()
    {
        Text = node.Text("Caption").Replace("&&", "\0").Replace("&", "").Replace("\0", "&"),
        FontSize = Math.Max(9, Math.Abs(node.Number("Font.Height", -11))),
        FontFamily = new FontFamily(node.Text("Font.Name", "Tahoma")),
        FontWeight = SetContains(node, "Font.Style", "fsBold") ? FontWeight.Bold : FontWeight.Normal,
        TextWrapping = node.Boolean("WordWrap") ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Foreground = Colour(node, "Font.Color", Brushes.Black)
    };

    private static TextBox Edit(DfmComponent node) => new()
    {
        Text = node.Text("Text"), IsReadOnly = node.Boolean("ReadOnly"),
        MaxLength = (int)Math.Max(0, node.Number("MaxLength")),
        Padding = new Thickness(2, 0),
        TextAlignment = node.Text("Alignment") == "taRightJustify" ? TextAlignment.Right : TextAlignment.Left,
        PasswordChar = node.Text("PasswordChar") is { Length: > 0 } password ? password[0] : '\0'
    };

    private static Control LabeledEdit(DfmComponent node)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = node.Text("EditLabel.Caption"), FontSize = 11 });
        stack.Children.Add(Edit(node));
        return stack;
    }

    private static TextBox Memo(DfmComponent node) => new()
    {
        Text = string.Join(Environment.NewLine, Strings(node, "Lines.Strings")),
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsReadOnly = node.Boolean("ReadOnly")
    };

    private Button Button(DfmComponent node)
    {
        var caption = Caption(node);
        var close = caption.Replace("_", "").Trim().ToLowerInvariant() is "cancel" or "close";
        var button = new Button
        {
            Content = caption.Length == 0 ? "…" : caption,
            Padding = new Thickness(2, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = close,
            IsCancel = close
        };
        if (close)
            button.Click += (_, _) => window?.Close();
        else
            ToolTip.SetTip(button, "This command has not been implemented.");
        return button;
    }

    private static ComboBox Combo(DfmComponent node)
    {
        var choices = Strings(node, "Items.Strings");
        return new ComboBox
        {
            ItemsSource = choices, SelectedIndex = Math.Clamp((int)node.Number("ItemIndex", -1), -1, choices.Length - 1),
            PlaceholderText = node.Text("Text"), Padding = new Thickness(2, 0)
        };
    }

    private Control Panel(DfmComponent node)
    {
        var grid = new Grid { Background = Background(node) };
        grid.Children.Add(CreateSurface(node));
        if (node.Boolean("ShowCaption", true) && node.Text("Caption").Length > 0)
            grid.Children.Add(new TextBlock
            {
                Text = Caption(node), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, FontSize = 11
            });
        return grid;
    }

    private static Control Group(DfmComponent node, Control content)
    {
        var grid = new Grid();
        grid.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#B7BCC4")),
            BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 0, 0)
        });
        grid.Children.Add(content);
        grid.Children.Add(new TextBlock
        {
            Text = Caption(node), FontSize = 11, Background = Background(node),
            Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
        });
        return grid;
    }

    private static Control RadioGroup(DfmComponent node)
    {
        var items = Strings(node, "Items.Strings");
        var canvas = new Canvas();
        var columns = Math.Max(1, (int)node.Number("Columns", 1));
        var rows = Math.Max(1, (int)Math.Ceiling(items.Length / (double)columns));
        var width = node.Number("Width", 150);
        var height = node.Number("Height", 120);
        for (var index = 0; index < items.Length; index++)
        {
            var radio = new RadioButton
            {
                Content = items[index].Replace("&", "_"),
                GroupName = node.InstanceName,
                IsChecked = index == (int)node.Number("ItemIndex", -1),
                FontSize = 11, Height = 20, MinHeight = 0,
                Width = Math.Max(20, width / columns - 12)
            };
            Canvas.SetLeft(radio, 8 + index / rows * width / columns);
            Canvas.SetTop(radio, 18 + index % rows * Math.Max(20, (height - 24) / rows));
            canvas.Children.Add(radio);
        }
        return Group(node, canvas);
    }

    private Control Pages(DfmComponent node)
    {
        var pages = node.Children.Where(child => child.ClassName is "TTabSheet" or "TTabSheet1").ToArray();
        return new TabControl
        {
            ItemsSource = pages.Select(page => new TabItem
            {
                Header = Caption(page), Content = new ScrollViewer { Content = CreateSurface(page) }
            }).ToArray(),
            SelectedIndex = Math.Max(0, Array.FindIndex(pages, page => page.InstanceName == node.Text("ActivePage")))
        };
    }

    private Control Tabs(DfmComponent node)
    {
        var grid = new Grid();
        var tabs = Strings(node, "Tabs.Strings");
        var strip = new TabControl
        {
            ItemsSource = tabs.Select(title => new TabItem { Header = title }).ToArray(),
            SelectedIndex = Math.Clamp((int)node.Number("TabIndex"), 0, Math.Max(0, tabs.Length - 1)),
            VerticalAlignment = VerticalAlignment.Top,
            Height = 28
        };
        grid.Children.Add(CreateSurface(node));
        grid.Children.Add(strip);
        return grid;
    }

    private static Control Slider(DfmComponent node) => new Slider
    {
        Minimum = node.Number("Min"), Maximum = Math.Max(node.Number("Min") + 1, node.Number("Max", 100)),
        Value = node.Number("Position"),
        Orientation = node.Text("Orientation") is "trVertical" or "sbVertical" ? Orientation.Vertical : Orientation.Horizontal
    };

    private static Control Shape(DfmComponent node) => new Border
    {
        Background = Colour(node, "Brush.Color", Brushes.Transparent),
        BorderBrush = Colour(node, "Pen.Color", Brushes.Gray),
        BorderThickness = new Thickness(node.Number("Pen.Width", 1))
    };

    private static Control UpDown(DfmComponent node) => new TextBlock
    {
        Text = "▴\n▾", FontSize = 8, TextAlignment = TextAlignment.Center,
        Foreground = Brushes.Gray, Background = Brushes.WhiteSmoke,
        IsEnabled = false
    };

    private Control Placeholder(DfmComponent node)
    {
        UnsupportedClasses.Add(node.ClassName);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E7ECF2")),
            BorderBrush = new SolidColorBrush(Color.Parse("#C4CDD8")),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = node.ClassName == "TListView" ? "List contents require runtime data" :
                    node.ClassName is "TImage32" or "TImgView32" or "TPaintBox32" or "TImage" ? "Image / canvas" : node.ClassName,
                FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#627083")),
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    public static string[] Strings(DfmComponent node, string property) =>
        node.Properties.TryGetValue(property, out var value) && value.Value is JsonArray list
            ? list.Select(item => item?["value"]?.GetValue<string>() ?? "").ToArray() : [];

    private static bool SetContains(DfmComponent node, string property, string item) =>
        node.Properties.TryGetValue(property, out var value) && value.Value is JsonArray set &&
        set.Any(entry => entry?.GetValue<string>() == item);
    private static string Caption(DfmComponent node) => node.Text("Caption").Replace("&", "_");
    private static IBrush Background(DfmComponent node) => Colour(node, "Color", new SolidColorBrush(Color.Parse("#F0F0F0")));

    private static IBrush Colour(DfmComponent node, string property, IBrush fallback)
    {
        if (!node.Properties.TryGetValue(property, out var value))
            return fallback;
        var named = value.Text();
        if (named.Length > 0)
            return named switch
            {
                "clBtnFace" => new SolidColorBrush(Color.Parse("#F0F0F0")),
                "clWindow" or "clWhite" => Brushes.White,
                "clWindowText" or "clBlack" => Brushes.Black,
                "clRed" => Brushes.Red, "clLime" => Brushes.Lime,
                "clBlue" => Brushes.Blue, "clGray" => Brushes.Gray,
                "clYellow" => Brushes.Yellow, "clSilver" => Brushes.Silver,
                _ => fallback
            };
        var colour = (int)value.Number(-1);
        return colour < 0 ? fallback : new SolidColorBrush(Color.FromRgb((byte)colour, (byte)(colour >> 8), (byte)(colour >> 16)));
    }

    private static string FontName(DfmComponent node) => node.Text("Font.Name", "Tahoma") is "MS Sans Serif" ? "Microsoft Sans Serif" : node.Text("Font.Name", "Tahoma");

    public static void AddCompactStyles(StyledElement target)
    {
        target.Classes.Add("dfm-ui");
        target.Styles.Add(new StyleInclude(new Uri("avares://OpenMFME.Desktop/"))
        {
            Source = new Uri("avares://OpenMFME.Desktop/Reconstruction/RecoveredControlStyles.axaml")
        });
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(CheckBox), typeof(RadioButton) })
        {
            var style = new Style(selector => selector.OfType(type));
            style.Setters.Add(new Setter(Layoutable.MinHeightProperty, 0d));
            style.Setters.Add(new Setter(Layoutable.MinWidthProperty, 0d));
            target.Styles.Add(style);
        }
    }
}

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using OpenMFME.Desktop.Reconstruction;
using OpenMFME.Formats;

namespace OpenMFME.Desktop;

/// <summary>A virtualized report list with recovered column widths and one shared horizontal viewport.</summary>
internal sealed class GameManagerTable : UserControl
{
    private sealed class Column : INotifyPropertyChanged
    {
        public string Caption { get; }
        public double DefaultWidth { get; }
        private GridLength width;
        public GridLength Width
        {
            get => width;
            set { width = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public Column(string caption, double width) { Caption = caption; DefaultWidth = width; this.width = new GridLength(width); }
    }

    private readonly Column[] columns;
    private readonly Func<GameMetadata, string> name;
    private readonly Action<string> sort;
    private readonly ScrollViewer viewport;
    private readonly Dictionary<string, Button> sortHeaders = [];
    public ListBox List { get; } = new() { Name = "GamesList", SelectionMode = SelectionMode.Single, Background = Brushes.White };

    public GameManagerTable(Func<GameMetadata, string> name, Action<string> sort)
    {
        this.name = name; this.sort = sort;
        var definition = RecoveredFormLibrary.Get("ManagerForm").Root.DescendantsAndSelf().Single(node => node.InstanceName == "GameList");
        columns = definition.Properties["Columns"].Value!.AsArray().Select(item =>
        {
            var properties = item!["properties"]!;
            return new Column(properties["Caption"]!["value"]!.GetValue<string>(), properties["Width"]!["value"]!.GetValue<double>());
        }).ToArray();
        Name = "GameTable";
        FontFamily = new FontFamily("Tahoma"); FontSize = 11;
        var dock = new DockPanel();
        var header = CreateGrid();
        header.Height = 25;
        for (var index = 0; index < columns.Length; index++)
        {
            var column = columns[index];
            var content = new Grid();
            var label = new TextBlock { Text = column.Caption, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
            if (column.Caption is "Game Name" or "Tech")
            {
                var button = new Button { Name = column.Caption == "Tech" ? "SortTechnology" : "SortGameName", Content = label,
                    Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch };
                button.Click += (_, _) => this.sort(column.Caption); content.Children.Add(button); sortHeaders[column.Caption] = button;
            }
            else content.Children.Add(label);
            var thumb = new Thumb { Name = $"ResizeColumn{index}", Width = 6, HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = new Cursor(StandardCursorType.SizeWestEast), Background = Brushes.Transparent,
                Template = new FuncControlTemplate<Thumb>((_, _) => new Border { Background = Brushes.Transparent }) };
            thumb.DragDelta += (_, e) => column.Width = new GridLength(Math.Clamp(column.Width.Value + e.Vector.X, 40, 800));
            content.Children.Add(thumb);
            var border = new Border { Background = new SolidColorBrush(Color.Parse("#F1F3F5")),
                BorderBrush = new SolidColorBrush(Color.Parse("#D6D9DD")), BorderThickness = new Thickness(0, 0, 1, 1), Child = content };
            Grid.SetColumn(border, index); header.Children.Add(border);
        }
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        var rows = new Style(selector => selector.OfType<ListBoxItem>());
        rows.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        rows.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
        rows.Setters.Add(new Setter(ListBoxItem.MinHeightProperty, 0d));
        List.Styles.Add(rows);
        ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Disabled);
        RefreshRows();
        dock.Children.Add(List);
        viewport = new ScrollViewer { Name = "GameTableScroll", HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Left, Content = dock };
        Content = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#BCC2C9")), BorderThickness = new Thickness(1), Child = viewport };
    }

    private Grid CreateGrid()
    {
        var grid = new Grid();
        foreach (var column in columns)
        {
            var definition = new ColumnDefinition();
            definition.Bind(ColumnDefinition.WidthProperty, new Binding(nameof(Column.Width)) { Source = column });
            grid.ColumnDefinitions.Add(definition);
        }
        return grid;
    }

    private Grid CreateRow(GameMetadata game)
    {
        var row = CreateGrid(); row.Height = 23;
        ToolTip.SetTip(row, game.FilePath);
        for (var index = 0; index < columns.Length; index++)
        {
            var caption = columns[index].Caption;
            var text = new TextBlock { Name = $"GameCell{index}", Text = caption switch
                { "Game Name" => name(game), "Tech" => game.System, _ => "" },
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0) };
            ToolTip.SetTip(text, text.Text);
            var border = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#E2E5E8")),
                BorderThickness = new Thickness(0, 0, 1, 1), Child = text };
            Grid.SetColumn(border, index); row.Children.Add(border);
        }
        return row;
    }

    public void RefreshRows() => List.ItemTemplate = new FuncDataTemplate<GameMetadata>((game, _) => game is null ? null : CreateRow(game));

    public void ResetColumns()
    {
        foreach (var column in columns) column.Width = new GridLength(column.DefaultWidth);
        viewport.Offset = new Vector(0, viewport.Offset.Y);
    }

    public void UpdateSort(string caption, bool descending)
    {
        foreach (var pair in sortHeaders)
            ((TextBlock)pair.Value.Content!).Text = pair.Key + (pair.Key == caption ? descending ? " ▼" : " ▲" : "");
    }

    public void UpdateNameHeader(bool fileNames) => ToolTip.SetTip(sortHeaders["Game Name"],
        fileNames ? "Showing configuration file names" : "Showing game names");
}

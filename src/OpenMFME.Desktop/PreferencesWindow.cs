using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpenMFME.Desktop.Services;
using OpenMFME.Formats;

namespace OpenMFME.Desktop;

public sealed class PreferencesWindow : Window
{
    private readonly SettingsStore store;
    private readonly bool mainFolderOnly;
    private readonly List<string> folders;
    private readonly ListBox list = new() { Name = "GameFolders", MinHeight = 120 };
    private readonly TextBox path = new() { Name = "FolderPath", PlaceholderText = "Paste a game-folder path…" };
    private readonly CheckBox recursive = new() { Name = "IncludeSubfolders", Content = "Look in subfolders for games" };
    private readonly CheckBox startup = new() { Name = "OpenBrowserAtStartup", Content = "Open Game Manager when OpenMFME starts" };
    private readonly TextBlock message = new() { Name = "PreferencesMessage", TextWrapping = TextWrapping.Wrap };
    public PreferencesWindow(SettingsStore store, bool mainFolderOnly = false)
    {
        this.store = store;
        this.mainFolderOnly = mainFolderOnly;
        folders = store.Current.GameFolders.ToList();
        recursive.IsChecked = store.Current.IncludeSubfolders;
        startup.IsChecked = store.Current.OpenGameBrowserOnStartup;
        Title = mainFolderOnly ? "Set Main Game Folder" : "Game browser settings"; Width = 760; Height = 520; MinWidth = 560; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = mainFolderOnly ? "Set the main game folder below; additional folders are kept." : "The first folder is the main game folder; the remaining entries are additional game folders.", TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = "Game folders", FontWeight = FontWeight.SemiBold });
        var input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        input.Children.Add(path);
        var browse = new Button { Content = "Browse…" }; Grid.SetColumn(browse, 1); input.Children.Add(browse);
        var add = new Button { Name = "AddFolder", Content = mainFolderOnly ? "Set main folder" : "Add" }; Grid.SetColumn(add, 2); input.Children.Add(add);
        header.Children.Add(input); DockPanel.SetDock(header, Dock.Top); content.Children.Add(header);
        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        var remove = new Button { Name = "RemoveFolder", Content = "Remove selected folder", IsEnabled = false };
        remove.IsVisible = !mainFolderOnly;
        footer.Children.Add(remove); footer.Children.Add(recursive); footer.Children.Add(startup);
        if (store.LoadNotice is not null) message.Text = store.LoadNotice;
        footer.Children.Add(message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Name = "CancelPreferences", Content = "Cancel", IsCancel = true };
        var save = new Button { Name = "SavePreferences", Content = "Save", IsDefault = true };
        buttons.Children.Add(cancel); buttons.Children.Add(save); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); content.Children.Add(footer);
        content.Children.Add(list); Content = content;
        RefreshFolders();
        list.SelectionChanged += (_, _) => remove.IsEnabled = list.SelectedItem is string;
        remove.Click += (_, _) => { if (list.SelectedItem is string selected) folders.Remove(selected); RefreshFolders(); };
        add.Click += (_, _) => AddFolder();
        browse.Click += async (_, _) =>
        {
            try
            {
                var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a game folder", AllowMultiple = false });
                if (selected.Count > 0)
                    path.Text = selected[0].TryGetLocalPath() ?? "";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            { message.Text = $"Unable to choose a folder: {exception.Message}"; }
        };
        cancel.Click += (_, _) => Close(false);
        save.Click += Save;
    }

    private void AddFolder()
    {
        try
        {
            var folder = (path.Text ?? "").Trim().Trim('"');
            if (!Path.IsPathFullyQualified(folder)) { message.Text = "Enter a full folder path."; return; }
            folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (!Directory.Exists(folder)) { message.Text = "That folder could not be found. Check the path."; return; }
            if (mainFolderOnly)
            {
                if (folders.Count > 0) folders.RemoveAt(0);
                folders.RemoveAll(value => GameLibraryScanner.PathComparer.Equals(value, folder));
                folders.Insert(0, folder); RefreshFolders(); path.Text = ""; message.Text = ""; return;
            }
            if (folders.Contains(folder, GameLibraryScanner.PathComparer)) { message.Text = "That folder is already in the list."; return; }
            folders.Add(folder); RefreshFolders(); path.Text = ""; message.Text = "";
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        { message.Text = $"Unable to add the folder: {exception.Message}"; }
    }
    private void RefreshFolders() => list.ItemsSource = folders.ToArray();
    private void Save(object? sender, RoutedEventArgs e)
    {
        // An entered path must be explicitly added, rather than silently lost on Save.
        if (!string.IsNullOrWhiteSpace(path.Text))
        { message.Text = "Apply the entered folder using the button beside it, or clear the path before saving."; return; }
        try
        {
            store.Save(new AppSettings { GameFolders = folders.ToArray(), IncludeSubfolders = recursive.IsChecked == true,
                OpenGameBrowserOnStartup = startup.IsChecked == true });
            Close(true);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { message.Text = $"Preferences could not be saved: {exception.Message}"; }
    }
}

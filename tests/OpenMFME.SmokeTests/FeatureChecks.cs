using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenMFME.Desktop;
using OpenMFME.Desktop.Services;
using OpenMFME.Formats;

internal static class FeatureChecks
{
    private sealed class Fixture : IDisposable
    {
        private readonly List<string> files = [];
        private readonly List<string> directories = [];
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "OpenMFME-checks-" + Guid.NewGuid().ToString("N"));
        public Fixture() { Directory.CreateDirectory(Root); directories.Add(Root); }
        public string Folder(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path); directories.Add(path); return path;
        }
        public string File(string name, string? content = null)
        {
            var path = Path.Combine(Root, name); files.Add(path);
            if (content is not null) System.IO.File.WriteAllText(path, content);
            return path;
        }
        public void Dispose()
        {
            // Remove only registered fixture files and empty directories; never recursively delete.
            foreach (var path in files) if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            foreach (var path in directories.AsEnumerable().Reverse()) Directory.Delete(path);
        }
    }

    private static void Pump(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("Feature check timed out");
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }
    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Click(Window window, string name) =>
        Named<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Snapshot(Window window, string? directory, string name)
    {
        if (directory is null) return;
        using var frame = window.CaptureRenderedFrame() ?? throw new Exception("Feature window has no rendered frame");
        using var destination = System.IO.File.Create(Path.Combine(directory, name + ".png"));
        frame.Save(destination, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    public static void Run(Action<bool, string> check, string? renderDirectory, string? sampleGame)
    {
        using var fixture = new Fixture();
        var games = fixture.Folder("games"); var child = fixture.Folder(Path.Combine("games", "child"));
        var partyPath = fixture.File(Path.Combine("games", "Party Time.gam"),
            "System ASTRASYSA1\r\nProtocol 1\r\nGame Party Time Player\r\nLayout party.fml\r\nROM first.bin\r\nROM second.bin\r\n");
        var legacyPath = fixture.File(Path.Combine("games", "child", "Legacy.GAM"));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        System.IO.File.WriteAllBytes(legacyPath, Encoding.GetEncoding(1252).GetBytes("Game Café £ Party\nSystem LEGACY\nLayout legacy.fml"));
        fixture.File(Path.Combine("games", "child", "Other.gam"), "game\tOther cabinet\nsystem SCORPION4\nlayout \"other cabinet.fml\"\nROM \"sound 1.bin\"\n");
        fixture.File(Path.Combine("games", "bad.gam"), "not a configuration");
        fixture.File(Path.Combine("games", "huge.gam"), new string('x', GameMetadata.MaximumBytes + 1));
        fixture.File(Path.Combine("games", "readme.txt"), "Game Ignored");

        var party = GameMetadata.Read(partyPath);
        check(party.DisplayName == "Party Time Player" && party.System == "ASTRASYSA1" && party.Protocol == "1",
            "Party Time metadata fields failed");
        check(party.Roms.SequenceEqual(new[] { "first.bin", "second.bin" }), "Repeated ROM entries were lost");
        check(GameMetadata.Read(legacyPath).Name == "Café £ Party", "Windows-1252 metadata decoding failed");
        var bomPath = fixture.File("unicode.gam");
        System.IO.File.WriteAllText(bomPath, "Game Unicode £\nSystem ASTRA", Encoding.Unicode);
        check(GameMetadata.Read(bomPath).Name == "Unicode £", "UTF-16 metadata decoding failed");
        var quoted = GameMetadata.Parse(" ; note\nGAME \"Party Time\"\nLAYOUT \"file with spaces.fml\"\nROM \"sound 1.bin\"", "quoted.gam");
        check(quoted.Name == "Party Time" && quoted.Layout == "file with spaces.fml" && quoted.Roms[0] == "sound 1.bin", "Quoted metadata failed");
        check(GameMetadata.Parse("System ASTRA", "fallback.gam").DisplayName == "fallback", "Filename title fallback failed");
        try { GameMetadata.Parse("Game bad\0file", "binary.gam"); throw new Exception("Binary metadata accepted"); }
        catch (InvalidDataException) { check(true, "Binary metadata rejected"); }

        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("Game Async metadata\nSystem ASTRA\nROM one.bin")))
        {
            var read = GameMetadata.ReadAsync(stream, "async.gam"); Pump(read);
            check(read.Result.Name == "Async metadata" && read.Result.Roms.Single() == "one.bin", "Asynchronous metadata inspection failed");
        }
        using (var stream = new MemoryStream(new byte[GameMetadata.MaximumBytes + 1]))
        {
            try { Pump(GameMetadata.ReadAsync(stream, "large.gam")); throw new Exception("Oversized asynchronous metadata accepted"); }
            catch (InvalidDataException) { check(true, "Asynchronous read size limit failed"); }
        }
        var original = System.IO.File.ReadAllBytes(partyPath);
        var scanTask = GameLibraryScanner.ScanAsync(new[] { games, child, Path.Combine(fixture.Root, "missing") }, true);
        Pump(scanTask); var scan = scanTask.Result;
        check(scan.Games.Length == 3, "Nested/overlapping game folders produced duplicates or lost files");
        check(scan.Warnings.Length == 3, "Malformed, oversized or missing inputs were not reported");
        var topTask = GameLibraryScanner.ScanAsync(new[] { games }, false); Pump(topTask);
        check(topTask.Result.Games.Length == 1, "Top-folder-only scanning included nested games");
        check(original.SequenceEqual(System.IO.File.ReadAllBytes(partyPath)), "Scanning modified a source game");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { Pump(GameLibraryScanner.ScanAsync(new[] { games }, true, cancellation.Token)); throw new Exception("Cancelled scan completed"); }
            catch (OperationCanceledException) { check(true, "Scan cancellation failed"); }
        }

        var settingsPath = fixture.File("settings.json");
        var store = new SettingsStore(settingsPath);
        check(store.Current.GameFolders.Length == 0 && !System.IO.File.Exists(settingsPath), "Defaults wrote settings on load");
        var changes = 0; store.Changed += (_, _) => changes++;
        store.Save(new AppSettings { GameFolders = [games, games + Path.DirectorySeparatorChar], IncludeSubfolders = false });
        var loaded = new SettingsStore(settingsPath);
        check(loaded.Current.GameFolders.Length == 1 && !loaded.Current.IncludeSubfolders && changes == 1, "Preferences round trip/deduplication failed");
        var brokenPath = fixture.File("broken-settings.json", "{invalid");
        var broken = new SettingsStore(brokenPath);
        check(broken.LoadNotice is not null && broken.Current.GameFolders.Length == 0 &&
            System.IO.File.ReadAllText(brokenPath) == "{invalid", "Broken settings were silently rewritten");
        var futurePath = fixture.File("future-settings.json", "{\"schemaVersion\":99}");
        check(new SettingsStore(futurePath).LoadNotice is not null, "Future settings schema was accepted");
        var nullPath = fixture.File("null-settings.json", "{\"gameFolders\":null}");
        check(new SettingsStore(nullPath).LoadNotice is not null, "Null folder list was accepted");
        var blockedDirectory = fixture.Folder("blocked-settings");
        var blocked = new SettingsStore(blockedDirectory);
        try { blocked.Save(new AppSettings { GameFolders = [games] }); throw new Exception("Saving onto a directory succeeded"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { check(blocked.Current.GameFolders.Length == 0, "Failed save changed in-memory settings"); }

        var notesDirectory = fixture.Folder("game-notes");
        var notesStore = new GameNotesStore(notesDirectory);
        fixture.File(Path.Combine("game-notes", Path.GetFileName(notesStore.FilePathFor(partyPath))));
        fixture.File(Path.Combine("game-notes", Path.GetFileName(notesStore.FilePathFor(legacyPath))));
        check(notesStore.Read(partyPath) is null && !System.IO.File.Exists(notesStore.FilePathFor(partyPath)),
            "Reading missing notes created a file");
        var serviceNotes = new GameNotes { Name = "Party Time Player", Manufacturer = "Astra", Notes = "Unicode café £\nSecond line" };
        notesStore.Save(partyPath, serviceNotes);
        check(new GameNotesStore(notesDirectory).Read(partyPath) == serviceNotes && notesStore.Read(legacyPath) is null,
            "Notes round trip or per-game isolation failed");
        if (OperatingSystem.IsWindows()) check(notesStore.FilePathFor(partyPath) == notesStore.FilePathFor(partyPath.ToUpperInvariant()),
            "Windows note identity is case sensitive");
        var noteBytes = System.IO.File.ReadAllBytes(notesStore.FilePathFor(partyPath));
        try { notesStore.Save(partyPath, serviceNotes with { Notes = new string('x', GameNotesStore.MaximumComments + 1) }); throw new Exception("Oversized notes accepted"); }
        catch (InvalidDataException) { check(noteBytes.SequenceEqual(System.IO.File.ReadAllBytes(notesStore.FilePathFor(partyPath))), "Invalid notes changed saved data"); }
        var malformedNote = fixture.File(Path.Combine("game-notes", Path.GetFileName(notesStore.FilePathFor(bomPath))), "{invalid");
        try { notesStore.Read(bomPath); throw new Exception("Malformed notes accepted"); }
        catch (System.Text.Json.JsonException) { check(System.IO.File.ReadAllText(malformedNote) == "{invalid", "Malformed notes were rewritten"); }
        var unsupportedNote = fixture.File(Path.Combine("game-notes", Path.GetFileName(notesStore.FilePathFor(Path.Combine(games, "unknown.gam")))),
            System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = 99, gamePath = partyPath, data = serviceNotes }));
        try { notesStore.Read(Path.Combine(games, "unknown.gam")); throw new Exception("Unknown note schema accepted"); }
        catch (InvalidDataException) { check(true, "Unknown note schema rejected"); }

        var savedBytes = System.IO.File.ReadAllBytes(settingsPath);
        var draft = new PreferencesWindow(store); draft.Show(); Dispatcher.UIThread.RunJobs();
        Named<TextBox>(draft, "FolderPath").Text = child; Click(draft, "SavePreferences");
        check(draft.IsVisible && store.Current.GameFolders.Length == 1, "Save discarded an entered but unadded folder");
        Click(draft, "AddFolder");
        Named<CheckBox>(draft, "OpenBrowserAtStartup").IsChecked = true;
        Click(draft, "CancelPreferences");
        check(!draft.IsVisible && store.Current.GameFolders.Length == 1 && savedBytes.SequenceEqual(System.IO.File.ReadAllBytes(settingsPath)),
            "Cancel committed draft preferences");

        var preferences = new PreferencesWindow(store); preferences.Show(); Dispatcher.UIThread.RunJobs();
        Named<TextBox>(preferences, "FolderPath").Text = child; Click(preferences, "AddFolder");
        Named<CheckBox>(preferences, "IncludeSubfolders").IsChecked = true;
        Named<CheckBox>(preferences, "OpenBrowserAtStartup").IsChecked = true;
        Snapshot(preferences, renderDirectory, "Preferences");
        Click(preferences, "SavePreferences");
        check(!preferences.IsVisible && store.Current.GameFolders.Length == 2 &&
            new SettingsStore(settingsPath).Current.OpenGameBrowserOnStartup, "Preferences UI did not save/reload");

        store.Save(store.Current with { OpenGameBrowserOnStartup = false });
        var main = new MainWindow(store); main.Show(); Dispatcher.UIThread.RunJobs();
        var fileMenu = main.FindControl<Menu>("MainMenuBar")!.ItemsSource!.Cast<MenuItem>().First();
        var fileEntries = fileMenu.ItemsSource!.Cast<Control>().ToArray();
        var originalPreferences = fileEntries.OfType<MenuItem>().Single(item => item.Name == "Preferences1");
        originalPreferences.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var recoveredPreferences = main.OwnedWindows.Single(window => window.Title == "Preferences — UI preview");
        check(recoveredPreferences is not PreferencesWindow && recoveredPreferences.GetVisualDescendants().OfType<Control>().Any(control => control.Name == "EffectsSlider"),
            "File Preferences no longer opens the recovered MFME preferences form");
        recoveredPreferences.Close();
        check(!fileEntries.Any(item => item.Name == "GameFolderSettings"), "File still contains Game folders");
        var managerEntry = main.FindControl<Menu>("MainMenuBar")!.ItemsSource!.Cast<MenuItem>()
            .Single(item => item.Name == "GameManager1");
        managerEntry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var browser = main.GameManager!;
        check(!main.IsVisible && browser.IsVisible, "Game Manager did not hide the main window");
        Pump(browser.ScanCompletion);
        var menus = Named<Menu>(browser, "ManagerMenu").ItemsSource!.Cast<MenuItem>().ToArray();
        check(menus.Length == 8, "Recovered Game Manager menu headers missing");
        var database = menus.Single(item => item.Name == "Scan1").ItemsSource!.Cast<Control>().OfType<MenuItem>().ToArray();
        database.Single(item => item.Name == "SetGameDirectory").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var workingSettings = browser.OwnedWindows.OfType<PreferencesWindow>().Single();
        check(workingSettings.Title == "Game browser settings", "Database folder settings route failed");
        Click(workingSettings, "CancelPreferences");
        database.Single(item => item.Name == "SetMainFolder1").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var primary = browser.OwnedWindows.OfType<PreferencesWindow>().Single();
        Named<TextBox>(primary, "FolderPath").Text = games;
        Click(primary, "AddFolder"); Click(primary, "SavePreferences");
        check(store.Current.GameFolders.SequenceEqual(new[] { games, child }), "Setting main folder lost additional folders");
        Pump(browser.ScanCompletion);
        database.Single(item => item.Name == "ScanDirectory").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pump(browser.ScanCompletion);
        managerEntry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(ReferenceEquals(main.GameManager, browser), "Game Manager was duplicated on repeated open");
        var list = Named<ListBox>(browser, "GamesList");
        check(list.ItemsSource!.Cast<GameMetadata>().Count() == 3, "Browser did not show scanned games");
        var findMenu = menus.Single(item => item.Name == "Find");
        findMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var findWindow = browser.OwnedWindows.Single(window => window.Title == "Find");
        findMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(browser.OwnedWindows.Count(window => window.Title == "Find") == 1, "Find window duplicated");
        check(!Named<Button>(findWindow, "Find").IsEnabled, "Blank Find query enabled");
        Named<TextBox>(findWindow, "Name").Text = "party";
        Click(findWindow, "Find"); var firstMatch = list.SelectedItem;
        Click(findWindow, "Find"); var secondMatch = list.SelectedItem;
        Click(findWindow, "Find");
        check(firstMatch is GameMetadata && !ReferenceEquals(firstMatch, secondMatch) && ReferenceEquals(list.SelectedItem, firstMatch),
            "Find did not cycle and wrap matching names");
        Named<TextBox>(findWindow, "Name").Text = "missing-name"; Click(findWindow, "Find");
        check(ReferenceEquals(list.SelectedItem, firstMatch) && Named<TextBlock>(findWindow, "DialogMessage").Text!.Contains("No matching"),
            "Find missing-name handling failed");
        Named<TextBox>(findWindow, "Name").Text = "party"; Dispatcher.UIThread.RunJobs();
        findWindow.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        findWindow.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        check(ReferenceEquals(list.SelectedItem, secondMatch), "Enter did not perform Find Next");
        Snapshot(findWindow, renderDirectory, "ManagerFind");
        findWindow.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        check(!findWindow.IsVisible, "Escape did not close Find");
        var filterMenu = menus.Single(item => item.Name == "Filter");
        filterMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var filterWindow = browser.OwnedWindows.Single(window => window.Title == "Filters");
        filterMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(browser.OwnedWindows.Count(window => window.Title == "Filters") == 1, "Filter window duplicated");
        Named<TextBox>(filterWindow, "FilterName").Text = "Party"; Dispatcher.UIThread.RunJobs();
        check(list.ItemsSource!.Cast<GameMetadata>().Count() == 2, "Recovered name filter failed");
        var platform = filterWindow.GetVisualDescendants().OfType<CheckBox>().Single(control => Equals(control.Tag, "LEGACY"));
        platform.IsChecked = false;
        check(list.ItemsSource!.Cast<GameMetadata>().Single().System == "ASTRASYSA1", "Platform exclusion failed");
        Snapshot(filterWindow, renderDirectory, "ManagerFilter");
        filterWindow.Close(); filterMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        filterWindow = browser.OwnedWindows.Single(window => window.Title == "Filters");
        check(Named<TextBox>(filterWindow, "FilterName").Text == "Party" &&
            filterWindow.GetVisualDescendants().OfType<CheckBox>().Single(control => Equals(control.Tag, "LEGACY")).IsChecked == false,
            "Reopened Filter lost its state");
        check(new[] { "ClearAllButton", "SelectAllButton", "ResetButton", "RefreshButton" }.All(name => Named<Button>(filterWindow, name).IsEnabled),
            "Working filter commands remain disabled");
        Click(filterWindow, "ClearAllButton"); check(!list.ItemsSource!.Cast<GameMetadata>().Any(), "Clear All platforms failed");
        Click(filterWindow, "SelectAllButton"); check(list.ItemsSource!.Cast<GameMetadata>().Count() == 2, "Select All platforms failed");
        Click(filterWindow, "ResetButton"); Dispatcher.UIThread.RunJobs(); check(list.ItemsSource!.Cast<GameMetadata>().Count() == 3, "Filter Reset failed");
        check(!Named<ComboBox>(filterWindow, "FilterTags").IsEnabled && !Named<CheckBox>(filterWindow, "DXs").IsEnabled,
            "Unsupported filter options are enabled");
        Click(filterWindow, "RefreshButton"); Pump(browser.ScanCompletion);
        check(list.ItemsSource!.Cast<GameMetadata>().Count() == 3, "Filter Refresh lost game list");
        filterWindow.Close();
        Named<TextBox>(browser, "GameSearch").Text = "Party"; Dispatcher.UIThread.RunJobs();
        check(list.ItemsSource!.Cast<GameMetadata>().Count() == 2, "Game search failed");
        Named<ComboBox>(browser, "SystemFilter").SelectedItem = "ASTRASYSA1"; Dispatcher.UIThread.RunJobs();
        check(list.ItemsSource!.Cast<GameMetadata>().Single().Name == "Party Time Player", "System filter failed");
        list.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        check(Named<TextBlock>(browser, "GameDetails").Text!.Contains("second.bin") &&
            Named<Button>(browser, "InspectGame").IsEnabled, "Selected game details failed");
        var notesMenu = menus.Single(item => item.Name == "AddNotes1");
        check(notesMenu.IsEnabled, "Add/Edit Notes is disabled for a selected game");
        notesMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var editor = browser.OwnedWindows.Single(window => window.Title!.StartsWith("Game Notes"));
        check(Named<TextBox>(editor, "Manufacturer").Text == "Astra" && Named<TextBox>(editor, "Notes").Text!.Contains("café"),
            "Game Notes editor did not load saved fields");
        Named<TextBox>(editor, "Name").Text = "Party Time Player (annotated)";
        Named<TextBox>(editor, "Technology").Text = "Astra System 1";
        Named<TextBox>(editor, "ReleaseDate").Text = "2000";
        Named<TextBox>(editor, "MachineType").Text = "Single cabinet";
        Named<TextBox>(editor, "Stakes").Text = "25p";
        Named<TextBox>(editor, "Prizes").Text = "£25";
        Named<TextBox>(editor, "Notes").Text = "Preservation notes\nUnicode £ café";
        Snapshot(editor, renderDirectory, "GameNotesEditor");
        check(Named<Button>(editor, "SpeedButton1").IsEnabled && !Named<TextBox>(editor, "Notes").IsReadOnly,
            "Notes editor is not editable");
        Click(editor, "SpeedButton1"); Pump(browser.ScanCompletion);
        var savedNotes = new GameNotesStore(notesDirectory).Read(partyPath)!;
        check(!editor.IsVisible && savedNotes.Name.EndsWith("(annotated)") && savedNotes.Prizes == "£25" && savedNotes.Notes.Contains("café"),
            "Game Notes Save failed");
        Named<TabControl>(browser, "GameInformationTabs").SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        check(Named<TextBlock>(browser, "GameNotesDetails").Text!.Contains("Preservation notes"), "Saved notes did not update Notes tab");
        check(browser.GetVisualDescendants().OfType<TextBlock>().Any(control => control.Text == savedNotes.Name),
            "Annotated name did not update the game list");
        notesMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        editor = browser.OwnedWindows.Single(window => window.Title!.StartsWith("Game Notes"));
        Named<TextBox>(editor, "Notes").Text = "discard this edit"; Click(editor, "SpeedButton2");
        check(notesStore.Read(partyPath) == savedNotes, "Notes Cancel saved the draft");
        notesMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        editor = browser.OwnedWindows.Single(window => window.Title!.StartsWith("Game Notes"));
        Named<TextBox>(editor, "Notes").Text = "discard on close"; editor.Close();
        check(notesStore.Read(partyPath) == savedNotes && original.SequenceEqual(System.IO.File.ReadAllBytes(partyPath)),
            "Closing notes saved the draft or changed source configuration");
        Named<TabControl>(browser, "GameInformationTabs").SelectedIndex = 0;
        store.Save(store.Current with { GameFolders = [games], IncludeSubfolders = false });
        Pump(browser.ScanCompletion);
        check(Named<TextBlock>(browser, "BrowserStatus").Text!.Contains("of 1 games"), "Preferences changes did not refresh the open browser");
        list.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        Snapshot(browser, renderDirectory, "GameBrowser");
        Click(browser, "InspectGame");
        check(main.IsVisible && !browser.IsVisible && main.FindControl<TextBlock>("LayoutSummary")!.Text!.Contains("Party Time Player"),
            "Inspecting a library game did not update the main window");
        managerEntry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var exiting = main.GameManager!;
        Named<Menu>(exiting, "ManagerMenu").ItemsSource!.Cast<MenuItem>().Single(item => item.Name == "ExitManager")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(main.IsVisible && main.GameManager is null, "Manager Exit did not restore main window");
        managerEntry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var closingManager = main.GameManager!;
        Named<Menu>(closingManager, "ManagerMenu").ItemsSource!.Cast<MenuItem>().Single(item => item.Name == "Filter")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var closingFilter = closingManager.OwnedWindows.Single(window => window.Title == "Filters");
        closingManager.Close();
        check(!closingFilter.IsVisible, "Closing Game Manager left its Filter window open");
        check(main.IsVisible && main.GameManager is null, "Manager window close did not restore main window");
        main.Close();
        store.Save(store.Current with { OpenGameBrowserOnStartup = true });
        var startupMain = new MainWindow(store); startupMain.Show(); Dispatcher.UIThread.RunJobs();
        var startupManager = startupMain.GameManager!;
        check(!startupMain.IsVisible && startupManager.IsVisible, "Startup Game Manager did not hide main window");
        startupManager.Close(); Dispatcher.UIThread.RunJobs();
        check(startupMain.IsVisible && startupMain.GameManager is null, "Exiting startup manager reopened it");
        startupMain.FindControl<Button>("BrowseGames")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var shutdownManager = startupMain.GameManager!;
        startupMain.Close(); Dispatcher.UIThread.RunJobs();
        Pump(shutdownManager.ScanCompletion);
        check(!startupMain.IsVisible && !shutdownManager.IsVisible && startupMain.GameManager is null,
            "Closing main window left Game Manager open or restored main window");


        var goodNotes = System.IO.File.ReadAllBytes(notesStore.FilePathFor(partyPath));
        System.IO.File.WriteAllText(notesStore.FilePathFor(partyPath), "{invalid");
        var damagedBrowser = new GameBrowserWindow(store, _ => { }, () => Task.CompletedTask);
        damagedBrowser.Show(); Dispatcher.UIThread.RunJobs(); Pump(damagedBrowser.ScanCompletion);
        Named<ListBox>(damagedBrowser, "GamesList").SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        Named<Menu>(damagedBrowser, "ManagerMenu").ItemsSource!.Cast<MenuItem>().Single(item => item.Name == "AddNotes1")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var damagedEditor = damagedBrowser.OwnedWindows.Single(window => window.Title!.StartsWith("Game Notes"));
        check(!Named<Button>(damagedEditor, "SpeedButton1").IsEnabled && Named<TextBlock>(damagedEditor, "DialogMessage").Text!.Contains("could not be loaded"),
            "Damaged notes can be silently overwritten");
        damagedEditor.Close(); damagedBrowser.Close();
        check(System.IO.File.ReadAllText(notesStore.FilePathFor(partyPath)) == "{invalid", "Opening damaged notes modified them");
        System.IO.File.WriteAllBytes(notesStore.FilePathFor(partyPath), goodNotes);
        var unwritableNotes = fixture.File("notes-root-file", "blocked");
        var failureBrowser = new GameBrowserWindow(store, _ => { }, () => Task.CompletedTask, new GameNotesStore(unwritableNotes));
        failureBrowser.Show(); Dispatcher.UIThread.RunJobs(); Pump(failureBrowser.ScanCompletion);
        Named<ListBox>(failureBrowser, "GamesList").SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        Named<Menu>(failureBrowser, "ManagerMenu").ItemsSource!.Cast<MenuItem>().Single(item => item.Name == "AddNotes1")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var failureEditor = failureBrowser.OwnedWindows.Single(window => window.Title!.StartsWith("Game Notes"));
        Named<TextBox>(failureEditor, "Notes").Text = "retain this draft"; Click(failureEditor, "SpeedButton1");
        check(failureEditor.IsVisible && Named<TextBox>(failureEditor, "Notes").Text == "retain this draft" &&
            Named<TextBlock>(failureEditor, "DialogMessage").Text!.Contains("could not be saved"), "Failed note save lost the draft or hid the error");
        failureBrowser.Close();
        check(!failureEditor.IsVisible && System.IO.File.ReadAllText(unwritableNotes) == "blocked", "Failed save changed its destination or left editor open");

        store.Save(store.Current with { GameFolders = [] });
        var empty = new GameBrowserWindow(store, _ => { }, () => Task.CompletedTask);
        empty.Show(); Dispatcher.UIThread.RunJobs(); Pump(empty.ScanCompletion);
        check(!Named<Menu>(empty, "ManagerMenu").ItemsSource!.Cast<MenuItem>().Single(item => item.Name == "AddNotes1").IsEnabled,
            "Add/Edit Notes enabled without a selected game");
        check(!Named<Button>(empty, "InspectGame").IsEnabled &&
            Named<TextBlock>(empty, "BrowserStatus").Text == "0 of 0 games", "Empty-library state failed");
        empty.Close();
        var closing = new GameBrowserWindow(store, _ => { }, () => Task.CompletedTask);
        closing.Show(); var pendingScan = closing.ScanCompletion; closing.Close(); Pump(pendingScan);
        check(!closing.IsVisible, "Closing a scanning browser did not finish cleanly");

        if (sampleGame is not null)
        {
            var before = System.IO.File.ReadAllBytes(sampleGame);
            var sample = GameMetadata.Read(sampleGame);
            store.Save(new AppSettings { GameFolders = [Path.GetDirectoryName(sampleGame)!] });
            var sampleBrowser = new GameBrowserWindow(store, _ => { }, () => Task.CompletedTask);
            sampleBrowser.Show(); Dispatcher.UIThread.RunJobs(); Pump(sampleBrowser.ScanCompletion);
            var sampleList = Named<ListBox>(sampleBrowser, "GamesList");
            var entry = sampleList.ItemsSource!.Cast<GameMetadata>().Single(game => GameLibraryScanner.PathComparer.Equals(game.FilePath, sample.FilePath));
            sampleList.SelectedItem = entry; Dispatcher.UIThread.RunJobs();
            check(entry.System == sample.System && entry.Roms.SequenceEqual(sample.Roms), "Real sample metadata changed during scanning");
            Snapshot(sampleBrowser, renderDirectory, "GameBrowser"); sampleBrowser.Close();
            var samplePreferences = new PreferencesWindow(store); samplePreferences.Show(); Dispatcher.UIThread.RunJobs();
            Snapshot(samplePreferences, renderDirectory, "Preferences"); samplePreferences.Close();
            check(before.SequenceEqual(System.IO.File.ReadAllBytes(sampleGame)), "Real sample game was modified");
            Console.WriteLine($"Verified sample: {sample.DisplayName} · {sample.System} · {sample.Roms.Length} ROM filenames.");
        }
    }
}

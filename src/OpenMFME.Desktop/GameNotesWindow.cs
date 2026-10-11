using System.Text.Json;
using Avalonia.Controls;
using OpenMFME.Desktop.Services;
using OpenMFME.Formats;

namespace OpenMFME.Desktop;

internal sealed class GameNotesEditor
{
    public Window Window { get; }

    public GameNotesEditor(GameNotesStore store, GameMetadata game, Action<GameNotes> saved)
    {
        var dialog = GameManagerDialogs.Create("GameNotes"); Window = dialog.Window;
        Window.Title = $"Game Notes — {game.DisplayName}";
        var save = GameManagerDialogs.Named<Button>(dialog.Surface, "SpeedButton1");
        var cancel = GameManagerDialogs.Named<Button>(dialog.Surface, "SpeedButton2");
        save.IsEnabled = true; ToolTip.SetTip(save, null);
        // Do not make Save the default: Enter in the multiline comments field inserts a newline.
        cancel.Click += (_, _) => Window.Close();
        var fields = new[] { "Name", "Manufacturer", "ReleaseDate", "Technology", "MachineType", "Stakes", "Prizes", "Notes" }
            .ToDictionary(name => name, name => GameManagerDialogs.Named<TextBox>(dialog.Surface, name));
        fields["Notes"].MaxLength = GameNotesStore.MaximumComments;
        try
        {
            var notes = store.Read(game.FilePath) ?? new GameNotes { Name = game.DisplayName, Technology = game.System };
            var values = new[] { notes.Name, notes.Manufacturer, notes.ReleaseDate, notes.Technology, notes.MachineType, notes.Stakes, notes.Prizes, notes.Notes };
            foreach (var pair in fields.Zip(values)) pair.First.Value.Text = pair.Second;
            dialog.Message.Text = "Save stores these details in OpenMFME. Cancel discards your changes.";
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            save.IsEnabled = false;
            foreach (var field in fields.Values) field.IsReadOnly = true;
            dialog.Message.Text = $"Notes could not be loaded: {exception.Message}";
        }
        save.Click += (_, _) =>
        {
            if (!save.IsEnabled) return;
            try
            {
                var notes = new GameNotes
                {
                    Name = fields["Name"].Text ?? "", Manufacturer = fields["Manufacturer"].Text ?? "",
                    ReleaseDate = fields["ReleaseDate"].Text ?? "", Technology = fields["Technology"].Text ?? "",
                    MachineType = fields["MachineType"].Text ?? "", Stakes = fields["Stakes"].Text ?? "",
                    Prizes = fields["Prizes"].Text ?? "", Notes = fields["Notes"].Text ?? ""
                };
                store.Save(game.FilePath, notes);
                saved(notes); Window.Close();
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
            { dialog.Message.Text = $"Notes could not be saved: {exception.Message}"; }
        };
        Window.Opened += (_, _) => fields["Name"].Focus();
    }
}

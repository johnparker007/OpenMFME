using System.Text.Json;
using Avalonia.Platform;
using OpenMFME.Formats;

namespace OpenMFME.Desktop.Reconstruction;

public static class RecoveredFormLibrary
{
    private static readonly Lazy<DfmCatalog> Data = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://OpenMFME.Desktop/Assets/recovered-forms.json"));
        return JsonSerializer.Deserialize<DfmCatalog>(stream, DfmCatalog.JsonOptions)
            ?? throw new InvalidDataException("The recovered form catalog is empty.");
    });
    public static DfmCatalog Catalog => Data.Value;
    public static RecoveredForm Get(string name) => Catalog.Forms.Single(form => form.Root.InstanceName == name);
}

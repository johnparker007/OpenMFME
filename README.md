# OpenMFME

Open source reimplementation of MFME. Useful for tech reference to derive future fruit machine emulation projects from.

## Current milestone

A runnable C# / Avalonia UI reconstruction:

- MFME's recovered main menu structure and meter strip.
- A searchable browser with first-pass previews of all 66 recovered forms.
- Standard controls reconstructed from their saved captions, geometry, properties and child hierarchy.
- Restored button glyphs, colour swatches, empty artwork canvases and inferred DIP-switch previews.
- Inherited fonts, compact range controls and tab headers, common docking/anchors and saved tab order.
- Saved OpenMFME preferences for game folders, subfolder scanning and opening the browser at startup.
- A Game Manager with recovered Find/Filter and Game Notes dialogs, name/platform filtering and read-only `.gam` layout information.
- Saved per-game details and comments, with a Notes tab and editable display names.
- A Game Manager report table with all 15 recovered columns, resizing, name/technology sorting, four preview tabs and working View commands.
- Read-only MFME v20.1 FML previews with saved artwork, labels, component outlines, a property inspector and Fit/100% views.

This is an interface milestone. Preferences and game browsing are independent OpenMFME features; original emulation and editing commands are not implemented. Recovered-form previews allow temporary edits; the separate working preferences window saves OpenMFME settings. Game configurations, layouts and ROMs are never modified by these features. Unimplemented commands are disabled. Runtime-generated artwork and several custom controls still need reconstruction.

The recorded form streams contain 4,464 components. See [the generated recovery report](docs/form-recovery.md) and [architecture notes](docs/architecture.md) for scope and limitations.

## Build and run

Install the .NET 8 SDK or a newer compatible SDK.

```powershell
dotnet build OpenMFME.sln -c Release
dotnet run --project src/OpenMFME.Desktop
```

On Windows, double-click `Run-OpenMFME.cmd` to build and launch. You can also open `OpenMFME.sln` in your C# IDE.

The application targets .NET 8 and Avalonia 12.1.4. NuGet restores packages on the first build. Windows is the verified desktop platform; Linux and macOS execution have not yet been tested.

Select **Game Manager** to enter its own window and menus; the main window is hidden until you exit. Use **Database → Set Main Game Folder** or **Additional Game Folders**, apply your folder choices, and choose **Save**. **Database → Scan All** rescans the library. **Exit** or the window close button returns to the main window. Search/filter the list, select a game, then choose **Load game** (or double-click it) to return to the main window and load its associated layout. **File → Load Game** also loads a single configuration file’s layout. Games cannot be played yet.

The main window starts with a blank, full-width canvas beneath its meter strip. Choose **File → Load Layout** to load a v20.1 `.fml` directly into this canvas. Background/bitmap artwork and labels render. **UI reconstruction → Inspect loaded layout…** opens the separate diagnostic viewer with component properties, optional outlines and zoom controls; reels, lamps and displays remain static. See [FML layout previews](docs/fml-layout-preview.md).

Open **UI reconstruction → Browse all recovered forms** to preview the original windows, including the original Preferences and Game Manager designs. **File → Preferences** opens the recovered MFME Preferences form. Other mapped menu entries, such as **Configuration**, also open previews.

Preferences are stored in the current user's local application-data directory under `OpenMFME/settings.json` (usually `%LOCALAPPDATA%\OpenMFME\settings.json` on Windows). Cancel leaves saved preferences unchanged. An open browser refreshes after preferences are saved. See [working preferences and game browsing](docs/preferences-and-game-browser.md).

## Validation

```powershell
dotnet run --project tests/OpenMFME.SmokeTests -c Release
```

The checks use Avalonia's headless platform and do not control your desktop. They cover binary-format edge cases, catalog invariants, the main menu, all 66 rendered form previews, preview command behavior, glyph source hashes, state-strip cropping, transparency, font inheritance, resizing, keyboard/control input, saved preferences, metadata decoding, folder scanning, browser interaction, upstream FML decoder fixtures and layout preview behavior. Optional screenshots:

```powershell
dotnet run --project tests/OpenMFME.SmokeTests -c Release -- --render-dir artifacts/previews
```

## Regenerate the form catalog

The original MFME executable is not needed to run OpenMFME. To reproduce the extraction from a local reference executable:

```powershell
dotnet run --project tools/OpenMFME.ExtractForms -- C:/path/to/MFME.exe src/OpenMFME.Desktop/Assets/recovered-forms.json docs/form-recovery.md src/OpenMFME.Desktop/Assets/recovered-images
```

Extraction treats the executable as data and never runs it. The source hash and file offsets tie each observation to that executable. The catalog records binary hashes and ranges. The optional image-directory argument extracts 41 glyph references (14 distinct bitmaps), the original icon and two image-list containers. See [graphics recovery](docs/graphics-recovery.md) for what is rendered and what remains archived.

## Structure

- `src/OpenMFME.Desktop`: application, form browser and standard-control mapper.
- `src/OpenMFME.Formats`: UI-independent form model and binary DFM parser.
- `src/OpenMFME.LayoutDecoder`: read-only FML decoding and UI-independent layout data.
- `third_party/mfme-fml-decoder`: pinned decoder source by Paul Maidment and John Parker, reused with author permission; see its attribution record.
- `tools/OpenMFME.ExtractForms`: extraction command and report generator.
- `tests/OpenMFME.SmokeTests`: parser checks and headless UI/rendering checks.

## Next milestones

1. Decode archived image-list masks and implement remaining custom-control equivalents.
2. Recover runtime-populated tabs/lists and remaining inherited properties/client-area defaults.
3. Render saved reel bands, lamp artwork and displays from the decoded layout model, alongside remaining game-library actions.
4. Connect a separately testable emulation engine, starting with Astra / Party Time Player.

# OpenMFME

Open source reimplementation of MFME. Useful for tech reference to derive future fruit machine emulation projects from.

## Current milestone

A runnable C# / Avalonia UI reconstruction:

- MFME's recovered main menu structure and meter strip.
- A searchable browser with first-pass previews of all 66 recovered forms.
- Standard controls reconstructed from their saved captions, geometry, properties and child hierarchy.
- Read-only inspection of MFME `.gam` metadata.

This is an interface milestone. Original commands and emulation are not implemented. Previews allow temporary edits to fields and selections; they do not save settings or modify layouts. Unimplemented commands are disabled. Custom graphics and controls are explicit placeholders.

The recorded form streams contain 4,464 components. See [the generated recovery report](docs/form-recovery.md) and [architecture notes](docs/architecture.md) for scope and limitations.

## Build and run

Install the .NET 8 SDK or a newer compatible SDK.

```powershell
dotnet build OpenMFME.sln -c Release
dotnet run --project src/OpenMFME.Desktop
```

On Windows, double-click `Run-OpenMFME.cmd` to build and launch. You can also open `OpenMFME.sln` in your C# IDE.

The application targets .NET 8 and Avalonia 12.1.4. NuGet restores packages on the first build. Windows is the verified desktop platform; Linux and macOS execution have not yet been tested.

Open **UI reconstruction → Browse all recovered forms** to preview the original windows. Main-menu entries such as **File → Preferences** and **Configuration** also open the corresponding previews. **File → Load Game** currently opens only the game-configuration metadata inspector.

## Validation

```powershell
dotnet run --project tests/OpenMFME.SmokeTests -c Release
```

The checks use Avalonia's headless platform and do not control your desktop. They cover binary-format edge cases, catalog invariants, the main menu, all 66 rendered form previews, and preview command behavior. Optional screenshots:

```powershell
dotnet run --project tests/OpenMFME.SmokeTests -c Release -- --render-dir artifacts/previews
```

## Regenerate the form catalog

The original MFME executable is not needed to run OpenMFME. To reproduce the extraction from a local reference executable:

```powershell
dotnet run --project tools/OpenMFME.ExtractForms -- C:/path/to/MFME.exe src/OpenMFME.Desktop/Assets/recovered-forms.json docs/form-recovery.md
```

Extraction treats the executable as data and never runs it. The source hash and file offsets tie each observation to that executable. Binary resources are recorded as hashes and ranges rather than bundled payloads.

## Structure

- `src/OpenMFME.Desktop`: application, form browser and standard-control mapper.
- `src/OpenMFME.Formats`: UI-independent form model and binary DFM parser.
- `tools/OpenMFME.ExtractForms`: extraction command and report generator.
- `tests/OpenMFME.SmokeTests`: parser checks and headless UI/rendering checks.

## Next milestones

1. Recover embedded graphics and implement custom-control equivalents.
2. Improve fonts, docking, inherited properties and runtime-populated tabs/lists.
3. Replace preview-only actions with independently implemented UI behavior.
4. Connect a separately testable emulation engine, starting with Astra / Party Time Player.

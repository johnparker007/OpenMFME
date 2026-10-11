# OpenMFME

Open source reimplementation of MFME. Useful for tech reference to derive future fruit machine emulation projects from.

## Current milestone

A runnable C# / Avalonia desktop shell, with menus, an About dialog, a recovered-form inventory, and a read-only inspector for MFME `.gam` metadata. It does not emulate machines or yet reproduce the original MFME windows.

The inventory records named binary-form headers from a locally supplied MFME executable. It is not a complete DFM extraction: control trees, assets and event associations are the next reconstruction milestone.

## Build and run

Install the .NET 8 SDK or a newer compatible SDK.

```powershell
dotnet build OpenMFME.sln -c Release
dotnet run --project src/OpenMFME.Desktop
```

On Windows, double-click `Run-OpenMFME.cmd` to build and launch the app. You can also open `OpenMFME.sln` in your C# IDE, or use the commands above. The application targets .NET 8 and Avalonia 12.1.4. NuGet restores packages on the first build. Windows is the initial verified platform; Linux and macOS execution have not yet been tested.

## Structure

- `src/OpenMFME.Desktop`: desktop application and cabinet workspace.
- `tools/Export-FormInventory.ps1`: repeatable form-header inventory tool.
- `docs/architecture.md`: scope and reconstruction approach.

To regenerate the form inventory from your own local reference executable:

```powershell
./tools/Export-FormInventory.ps1 -ExecutablePath 'C:/path/to/MFME.exe'
```

The executable, ROMs and layouts are not included. The inventory includes a source hash so observations can be tied to a particular executable.

## Next milestones

1. Parse full DFM control trees and extract relevant UI assets.
2. Reconstruct the main MFME window, then standard dialogs from those descriptions.
3. Implement custom cabinet controls such as lamps, reels and displays.
4. Connect a separately testable emulation engine, starting with Astra / Party Time Player.

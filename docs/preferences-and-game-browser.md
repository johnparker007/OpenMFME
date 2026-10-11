# Working preferences and game browsing

These are independent OpenMFME features. They do not recover or execute MFME's original event handlers. The original GeneralConfig and ManagerForm designs remain accessible through the recovered-form browser.

## Preferences

File → Preferences opens the recovered MFME Preferences preview. Folder settings are accessed within Game Manager: Database → Set Main Game Folder replaces the first configured folder while retaining additional folders; Database → Additional Game Folders opens the complete folder list. The first list entry is the main folder.

This settings window supports game-folder paths, searching subfolders and opening the game browser at startup. Paste an absolute path and choose Add, or use Browse then Add. Duplicate normalized paths are ignored. Remove affects the preference list, not the directory itself. Save commits the draft; Cancel/window close discards it. An entered but unadded path must be added or cleared before Save, so it is not silently lost.

Defaults use an empty folder list, include subfolders and leave startup browsing off. Settings are versioned JSON at LocalApplicationData/OpenMFME/settings.json; on Windows this is normally `%LOCALAPPDATA%\OpenMFME\settings.json`. No settings file is created merely by launching. Missing settings use defaults; malformed, unreadable or unsupported settings show a notice and are left alone until an explicit Save. Saves use a temporary file in the settings directory, followed by replacement. Failed saves leave live settings unchanged. These preferences are separate from original MFME settings.

## Browser

Game Manager and Browse games hide the main window and open one Game Manager window with the recovered ManagerMenu. Exit, window close and inspecting a game restore the main window. Startup enters this mode once, so exiting does not reopen it. Unimplemented menu commands remain disabled. It scans only configured folders. Refresh rescans; Stop scan cancels and keeps previous results. Saving preferences automatically rescans an open browser. Closing it cancels its scan and removes its settings subscription. Preferences invoked from a browser are owned by that browser.

Only .gam files are inspected; extension matching is case insensitive. Recursive and top-folder-only modes are supported. Overlapping folders are deduplicated using native path comparison. Directory links/reparse points and inaccessible subfolders are skipped. Unavailable root folders and unreadable/malformed configuration files appear in scan details. Unavailable/inaccessible folders are not equivalent to an empty collection; the details describe skip behaviour.

Scanning uses a worker task, cancellation and progress messages. Cancellation is checked between enumeration/file operations; an operating-system read or network-folder enumeration already in progress may take time to return. There is no persistent library database yet: every refresh rereads metadata. The issue view displays the first 100 reports; its total count includes all reports.

Find opens the recovered ManagerFindForm design. Enter a name and choose Find (or press Enter) to select the next matching game in the current visible list, wrapping at the end. It leaves the list filters unchanged and reports when no name matches. Escape closes the window.

Filter opens the recovered FilterForm design. Name and platform checkboxes narrow the list immediately; Clear All and Select All affect the displayed platforms, Reset clears these dialog filters, and Refresh rescans configured folders. Dialog filters intersect with the toolbar search/system filter. They survive closing/reopening the dialog during the current Game Manager session; new platforms found by a scan start included. Tags, WIP/non-runner and layout categories remain disabled until their metadata is supported. Closing Game Manager closes its owned dialogs.

Search matches game name, system and configuration path. The system filter narrows results. Selecting a game shows its game/system/protocol fields, layout filename, ROM filenames and configuration path. Load game (or double-click) closes the browser and automatically loads the associated v20.1 FML artwork into the main canvas. Loading errors appear in a dialog and preserve any existing canvas. No ROM execution, file association or layout conversion is performed. See [FML layout previews](fml-layout-preview.md).

## Game Manager view

The report table uses the recovered ManagerForm's 15 column captions and default widths: Game Name, Rating, Version, Tech, Played, Total In, Total Out, %, WIP, DX, Multi Player, Lo Tech, Tags, Last Played and Status. Name and Tech are populated from available metadata; unavailable fields remain blank. Drag the divider at the right edge of a column header to resize it. Click Game Name or Tech to sort; click again to reverse the direction. Rows are virtualized, and headers/rows share a horizontal viewport so the later columns remain accessible.

View → Show File Names toggles configuration filenames in the name column and preserves selection. Search continues to match both game names and paths; Find searches game names. View → Default Columns restores the recovered widths and scrolls the table back to its left edge. View → Default Window restores the 1280 × 760 window, original position and 3:1 table/preview split. A draggable splitter changes the pane proportions. View choices and widths apply to the current manager session.

The right-hand tabs are Game, Cabinet, Flyer and Notes. Game currently shows configuration information, Notes shows saved OpenMFME notes, and Cabinet/Flyer explicitly show placeholders until artwork loading is implemented. The lower information panel shows the selected game's name, manufacturer, release date, technology, machine type, stakes and prizes from configuration/saved notes; missing values stay blank. Clearing the selection clears the panel. No play counts or financial totals are inferred from missing emulation data.

## Game notes

Select a game and choose Add/Edit Notes to open the recovered GameNotes editor. It supports name, manufacturer, release date, technology, machine type, stakes, prizes and multiline comments. Save updates the list and Notes tab; Cancel, Escape and window close discard the draft. The editor belongs to Game Manager and closes with it. Edited display names are used by Find and the name/search filters; the original configuration name remains visible in the Game tab. Technology is descriptive metadata and does not change the configuration system or platform filter.

OpenMFME notes are independent JSON files under LocalApplicationData/OpenMFME/game-notes. Each file is keyed by a hash of the absolute .gam path (case insensitive on Windows). Moving a configuration gives it a new identity; note migration and importing MFME's existing notes/database are not implemented. Game configurations, layouts and ROMs are never edited. Each metadata field supports 120 characters; comments support 32,768 characters. Note files are bounded to 256 KiB and saved by replacing from a temporary file. Failed saves retain the editor draft and show the error. Invalid or unreadable notes are reported in scan details and the editor disables Save so they cannot be silently overwritten.

## Metadata reader

The bounded reader accepts at most 256 KiB per configuration. It supports BOM-marked UTF-8/UTF-16, strict unmarked UTF-8 with Windows-1252 fallback, whitespace-separated field names and values, quoted values, repeated ROM lines and comments starting with semicolon, hash or //. Unknown fields are ignored. System/Game/Layout/Protocol use the last saved value, while ROM entries preserve order. Files with binary nulls or no recognized fields are reported as invalid. A missing Game field falls back to the configuration filename.

This is a metadata parser, not a complete parser for every MFME configuration field. Layout/ROM names are treated as descriptive strings; referenced files are not opened or validated.

## Verification

Isolated checks cover settings round trips, normalization, invalid/future settings, failed saves, draft cancellation, saving through the UI, automatic library refresh, missing/oversized/malformed files, overlapping folders, recursion, legacy encoding, cancellation, search/system filtering, startup browsing, duplicate-window prevention, main-window inspection, table sorting/resizing, View commands, metadata panels, four tabs and virtualized large-library navigation. Temporary fixture cleanup removes registered files and empty directories without recursive deletion. Tests do not write real user preferences.

An optional local sample can be verified read-only and used in preview screenshots:

```powershell
dotnet run --project tests/OpenMFME.SmokeTests -c Release -- --render-dir artifacts/previews --sample-game C:/path/to/game.gam
```

The supplied Party Time Player configuration was separately checked: it reports ASTRASYSA1, Protocol 1 and two ROM filenames. Its bytes were unchanged after inspection and scanning. Local game files are not included in the repository.

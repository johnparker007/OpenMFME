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

Search matches game name, system and configuration path. The system filter narrows results. Selecting a game shows its game/system/protocol fields, layout filename, ROM filenames and configuration path. Open layout information (or double-click) updates the main window and closes the browser. No ROM execution, artwork loading, file association or layout conversion is performed.

## Metadata reader

The bounded reader accepts at most 256 KiB per configuration. It supports BOM-marked UTF-8/UTF-16, strict unmarked UTF-8 with Windows-1252 fallback, whitespace-separated field names and values, quoted values, repeated ROM lines and comments starting with semicolon, hash or //. Unknown fields are ignored. System/Game/Layout/Protocol use the last saved value, while ROM entries preserve order. Files with binary nulls or no recognized fields are reported as invalid. A missing Game field falls back to the configuration filename.

This is a metadata parser, not a complete parser for every MFME configuration field. Layout/ROM names are treated as descriptive strings; referenced files are not opened or validated.

## Verification

Isolated checks cover settings round trips, normalization, invalid/future settings, failed saves, draft cancellation, saving through the UI, automatic library refresh, missing/oversized/malformed files, overlapping folders, recursion, legacy encoding, cancellation, search/system filtering, startup browsing, duplicate-window prevention and main-window inspection. Temporary fixture cleanup removes registered files and empty directories without recursive deletion. Tests do not write real user preferences.

An optional local sample can be verified read-only and used in preview screenshots:

```powershell
dotnet run --project tests/OpenMFME.SmokeTests -c Release -- --render-dir artifacts/previews --sample-game C:/path/to/game.gam
```

The supplied Party Time Player configuration was separately checked: it reports ASTRASYSA1, Protocol 1 and two ROM filenames. Its bytes were unchanged after inspection and scanning. Local game files are not included in the repository.

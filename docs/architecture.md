# Architecture and reconstruction scope

## Current implementation

The UI-independent `OpenMFME.Formats` library parses Delphi `TPF0` form streams into a neutral component model. Each component preserves its class, instance name, filer flags, child position, typed properties and children. The extractor scans the supplied unpacked executable because the forms are available in its file data.

The desktop application reads a checked-in JSON catalog of that neutral model. It does not interpret original DFM streams at runtime or require MFME.exe. A first-pass Avalonia mapper creates standard controls from the model. This is a reconstruction preview, not the final handwritten implementation of each feature.

Main-menu captions and hierarchy come from the recovered main form. Explicit command mappings open corresponding previews, except Game Manager, which opens the working browser. File → Preferences opens the recovered MFME Preferences form; Game Manager’s Database menu opens saved game-folder settings. Load Game reads metadata and loads its associated FML into the main canvas; Load Layout loads an FML directly, and About describes OpenMFME. Unimplemented leaf commands are disabled. Menu-to-window routing is newly implemented and does not execute original event handlers.

## Observed recovery

The supplied executable yielded 66 complete form streams, 4,464 components and one rejected signature. The source hash, exact consumed byte ranges and rejected signature are recorded. [The generated catalog report](form-recovery.md) lists every form and component class.

The parser handles the value types encountered in these streams, including signed numbers, strings, sets, lists, collections, binary payload ranges and floating-point values. Length checks and nesting limits reject malformed data. Extended floating-point values are converted to .NET doubles; ANSI strings use Latin-1. No claim of universal support for every Delphi/FPC dialect is made.

Relevant format references: [Embarcadero ObjectBinaryToText](https://docwiki.embarcadero.com/Libraries/Sydney/en/System.Classes.ObjectBinaryToText) and the independently published [Free Pascal reader implementation](https://github.com/fpc/FPCSource/blob/master/rtl/objpas/classes/reader.inc). The parser is a new C# implementation; Delphi and FPC value-number extensions differ.

## Preview limitations

- Original handler names are preserved as references, not recovered implementations.
- Nonvisual components (timers, dialogs, HTTP clients, image lists) are inventoried but not started.
- Embedded binary assets retain their length, offset and SHA-256. Glyph bitmaps are bundled and rendered; the icon and two image-list containers are archived. See [graphics recovery](graphics-recovery.md).
- Colour swatches and empty image canvases are reconstructed. DIP-switch appearance is inferred rather than recovered from custom-control code. Remaining custom visual components and runtime-populated list/tree views use placeholders.
- Saved visibility, captions, geometry, enabled state, common field values, parent fonts, common docking/anchors and tab order are mapped. Border/client-area defaults, resize constraints and specialized properties remain approximate. See [layout reconstruction](layout-reconstruction.md).
- Runtime-generated tabs and data cannot be reconstructed from DFM alone. For example, the saved Game Configuration tab list contains only “None”; the original code populates it later.
- Fields and selections in recovered previews can be edited temporarily; their original commands are disabled except closing. The separate working preferences window saves only OpenMFME settings. Browsing/inspection never modifies game configurations, layouts or ROMs.

Compact checkbox, radio, scrollbar, slider and tab-header templates accommodate the original geometry. Other controls use Avalonia's theme; native font metrics and custom rendering still differ, so this is not a pixel-identical clone.

## Working application features

A UI-independent game metadata reader and folder scanner live in OpenMFME.Formats. SettingsStore owns OpenMFME preferences, and the new PreferencesWindow/GameBrowserWindow implement working flows alongside the recovered previews. File scanning runs on a worker task with cancellation, while browser controls remain on the UI thread. Scan generations prevent older results/progress replacing a newer scan. Closed browsers unsubscribe and cancel outstanding scans.

Settings are written through a temporary file in the same directory and replaced only after serialization succeeds. A failed save leaves the live settings unchanged. Missing/malformed preferences load defaults with a notice; no rewrite happens merely on load. [Feature details and limits](preferences-and-game-browser.md) describe storage, supported metadata and scanning.

The UI-independent `OpenMFME.LayoutDecoder` wraps the pinned MFME v20.1 decoder by Paul Maidment and John Parker. It returns saved geometry, properties and image bytes; shared Avalonia rendering lives in `LayoutSurface`. The main window displays saved artwork, reel strips and graphic lamp off images on its full-width canvas; the separate `LayoutPreviewWindow` inspector is available under UI reconstruction. Decoder calls run on a worker task and are serialized because upstream logging configuration is static. Closed viewers cancel loading and release owned bitmap resources. LayoutReelControl and LayoutLampControl draw cached images from explicit output states. The optional main-window rendering test uses a monotonic clock and UI timer; it is demonstration code, independent of future emulated timing, and stops on hide, successful layout replacement or close. See [FML layout previews](fml-layout-preview.md) for validation and current rendering limits.

## Boundaries for subsequent work

Keep layout data independent of Avalonia controls. A layout model should describe artwork, inputs, lamps, reels and displays, with the renderer responsible for drawing it.

The future emulation engine must run without a window. Its interface should accept machine inputs and advances in emulated time, and expose output states and audio. Do not use UI timers to establish emulated timing.

Keep local binaries, ROMs, layouts and disassembler databases outside the repository, or in ignored `reference/`. Record observations with source hashes and distinguish observed behavior from inference.

## Verification

A standalone smoke-check executable constructs and renders all 66 previews through Avalonia's headless backend. Synthetic binary fixtures check UTF-8, signed numbers, inherited child ordering, collections and binary ranges; malformed/truncated/deeply nested data must fail. Tests also validate the main menu, form catalog, preview Cancel behavior, glyph hashes/transparency, nested font inheritance, docking, anchors, real-window resizing, Tab navigation and compact range-control input. It can export representative PNGs for visual review without moving the user's mouse or opening visible windows.

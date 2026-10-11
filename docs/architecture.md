# Architecture and reconstruction scope

## Current implementation

The UI-independent `OpenMFME.Formats` library parses Delphi `TPF0` form streams into a neutral component model. Each component preserves its class, instance name, filer flags, child position, typed properties and children. The extractor scans the supplied unpacked executable because the forms are available in its file data.

The desktop application reads a checked-in JSON catalog of that neutral model. It does not interpret original DFM streams at runtime or require MFME.exe. A first-pass Avalonia mapper creates standard controls from the model. This is a reconstruction preview, not the final handwritten implementation of each feature.

Main-menu captions and hierarchy come from the recovered main form. Explicit command mappings open corresponding previews. The Load Game entry currently inspects metadata, and About describes OpenMFME. Unimplemented leaf commands are disabled. Menu-to-window routing is newly implemented and does not execute original event handlers.

## Observed recovery

The supplied executable yielded 66 complete form streams, 4,464 components and one rejected signature. The source hash, exact consumed byte ranges and rejected signature are recorded. [The generated catalog report](form-recovery.md) lists every form and component class.

The parser handles the value types encountered in these streams, including signed numbers, strings, sets, lists, collections, binary payload ranges and floating-point values. Length checks and nesting limits reject malformed data. Extended floating-point values are converted to .NET doubles; ANSI strings use Latin-1. No claim of universal support for every Delphi/FPC dialect is made.

Relevant format references: [Embarcadero ObjectBinaryToText](https://docwiki.embarcadero.com/Libraries/Sydney/en/System.Classes.ObjectBinaryToText) and the independently published [Free Pascal reader implementation](https://github.com/fpc/FPCSource/blob/master/rtl/objpas/classes/reader.inc). The parser is a new C# implementation; Delphi and FPC value-number extensions differ.

## Preview limitations

- Original handler names are preserved as references, not recovered implementations.
- Nonvisual components (timers, dialogs, HTTP clients, image lists) are inventoried but not started.
- Embedded binary assets retain their length, offset and SHA-256. Glyph bitmaps are bundled and rendered; the icon and two image-list containers are archived. See [graphics recovery](graphics-recovery.md).
- Colour swatches and empty image canvases are reconstructed. DIP-switch appearance is inferred rather than recovered from custom-control code. Remaining custom visual components and runtime-populated list/tree views use placeholders.
- Saved visibility, captions, explicit geometry, enabled state and common field values are mapped. Docking, inherited font/property behavior and many specialized properties remain approximate.
- Runtime-generated tabs and data cannot be reconstructed from DFM alone. For example, the saved Game Configuration tab list contains only “None”; the original code populates it later.
- Fields and selections can be edited temporarily. Original commands are disabled except closing a preview. No settings, layouts or ROM files are modified.

The compact checkbox/radio templates accommodate the original control geometry. Other controls use Avalonia's theme, so this milestone is not a pixel-identical clone.

## Boundaries for subsequent work

Keep layout data independent of Avalonia controls. A layout model should describe artwork, inputs, lamps, reels and displays, with the renderer responsible for drawing it.

The future emulation engine must run without a window. Its interface should accept machine inputs and advances in emulated time, and expose output states and audio. Do not use UI timers to establish emulated timing.

Keep local binaries, ROMs, layouts and disassembler databases outside the repository, or in ignored `reference/`. Record observations with source hashes and distinguish observed behavior from inference.

## Verification

A standalone smoke-check executable constructs and renders all 66 previews through Avalonia's headless backend. Synthetic binary fixtures check UTF-8, signed numbers, inherited child ordering, collections and binary ranges; malformed/truncated/deeply nested data must fail. Tests also validate the main menu, form catalog and preview Cancel behavior. It can export representative PNGs for visual review without moving the user's mouse or opening visible windows.

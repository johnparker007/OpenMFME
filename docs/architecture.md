# Architecture and reconstruction scope

## Current implementation

The desktop project contains the application shell. It reads game configuration metadata but does not load or execute ROMs. The form inventory is read-only and lists named form headers; it does not pretend those dialogs have been recreated.

C# event handlers currently implement the small shell commands. Avoid introducing a large UI framework until full form extraction tells us what is needed.

## Boundaries for subsequent work

Keep imported layout data independent of Avalonia controls. A layout model should describe artwork, inputs, lamps, reels and displays, with the renderer responsible for drawing it.

The eventual emulation engine must run without a window. Its interface should accept machine inputs and advances in emulated time, and expose output states and audio. The desktop project will adapt those states to the cabinet view. Do not use UI timers to establish emulated timing.

The DFM parser should preserve control type, instance name, properties, child hierarchy and event-handler references. Generated Avalonia views will need a mapping for standard VCL controls; custom MFME controls require explicit implementations. An event-handler name is a reference, not recovered source code.

## Form inventory limitations

The initial inventory scans for `TPF0` signatures followed by plausible class and instance names. These are candidate binary-form headers, not fully validated form streams. Embedded/inherited forms and custom components require a real stream parser. Offsets are hexadecimal file offsets, not loaded virtual addresses.

## Reference material

Keep local binaries, ROMs, layouts and disassembler databases outside the repository (or in ignored `reference/`). Record findings with the source hash and distinguish observed behavior from inferred behavior.

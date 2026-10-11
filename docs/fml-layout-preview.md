# Read-only MFME v20.1 layout previews

## Opening a layout

The main window starts with a blank canvas directly beneath the meter strip, with no welcome content, sidebar or bottom status bar. Choose **File → Load Layout** and select a `.fml` file. **File → Load Game**, or **Load game** / double-click in Game Manager, reads a `.gam` and automatically loads its associated FML into the main canvas. The title updates when loading succeeds. Artwork fits the available area with its aspect ratio preserved and starts at the top left.

The main canvas shows background/bitmap artwork and saved labels without diagnostic outlines. **UI reconstruction → Inspect loaded layout…** opens the separate diagnostic viewer. Select a component in its list or click its outline to inspect decoded properties, including reel band offsets and lamp assignments. **Fit** scales the saved canvas to the window; **100%** uses scrollbars at native layout size.

Loads run in the background. A new load cancels an earlier pending load; closing cancels loading and releases bitmap resources. A failed load reports its error in a dialog and preserves the current canvas. Neither flow runs ROMs.

## Decoder provenance

This uses [mfme-fml-decoder](https://github.com/paul-maidment/mfme-fml-decoder), written by Paul Maidment and John Parker, at revision `93101f3fa5792b299f13fc7377c7aac79e0c73b5`. John authorized reuse in this project. The vendored source and permission record are in `third_party/mfme-fml-decoder`; no new upstream licence is inferred.

The Decoder, Decryption, Model and selected Utilities source is compiled into a separate library. Three marked local changes reject invalid record lengths before allocation in FileWalker, MfmeVersionReader and ComponentWalker. The command-line application and exporters are excluded. OpenMFME exposes reading only.

The wrapper checks the encrypted chunk envelope and declared sizes before decompression. Input is limited to 128 MiB and declared decoded data to 256 MiB. Geometry and decoded image sizes are checked before display. These checks are defensive limits, not a comprehensive sandbox for every upstream decoder operation. Cancellation is checked while reading and between decoder stages; an in-progress upstream stage can finish before cancellation takes effect.

The decoder requires layouts saved by **MFME v20.1**. Older layouts show a resave message. OpenMFME does not convert or overwrite them.

## Party Time verification

The supplied `Party_Time_Player_(Astra)_[Dx02_5jp].gam` and associated v20.1 FML decode to **121 components** on a **916 × 1577** saved background:

| Type | Count |
| --- | ---: |
| Background | 1 |
| Reel | 6 |
| Button | 5 |
| Checkbox | 5 |
| SevenSeg | 4 |
| AlphaNew | 1 |
| Lamp | 99 |

Ten components are outside the saved background. The preview clips artwork and outlines to the saved canvas, while the inspector retains all components. No ROM is loaded or executed. The headless check compares the FML hash before and after viewing to verify it is unchanged. The game layout and ROMs are not bundled in this repository.

Five small upstream fixtures cover Background, Lamp, Reel, Label and Button decoding, including angled controls. Tests compare decoded property trees against upstream JSON, normalizing only the zero background OffsetX/OffsetY defaults introduced by the pinned revision. Viewer checks cover labels, missing files, selection, zoom, outlines, closing during loading and blank startup, direct main-canvas loading, Game Manager routing, resizing, replacement loads, failed-load preservation and closing during a load.

To run the optional real-layout checks and export a preview:

```powershell
dotnet run --project tests/OpenMFME.SmokeTests -c Release -- --sample-game "C:/path/to/Party_Time_Player_(Astra)_[Dx02_5jp].gam" --render-dir artifacts/previews
```

## Rendering scope

The main canvas and inspector render saved background/bitmap artwork, reel bands, reel overlays, lamp off artwork and labels. Buttons, checkboxes and displays retain their geometry/properties but have no machine behavior. Pure green (`#00FF00`) is keyed out of reel overlays and graphic lamp images when the saved lamp Transparent flag is enabled. Existing alpha is preserved. Opaque lamp images retain their green pixels.

Each reel displays a wrapped three-symbol band window (or fewer when the strip has fewer stops). Saved Stops, BandOffset, Reversed and horizontal/vertical orientation determine its strip selection. The saved gradient is interpreted as grayscale light transmission; OffLevel controls the global unlit reel brightness when LampsEnabled is set. Frames are layered over the strip with green transparency. The strip is currently stretched into a flat viewport: MFME’s cylindrical projection, ReelHeight/WidthDiff, custom reel modes, winlines, per-symbol illumination masks and motor step calibration are not reproduced yet. The band-offset unit is provisionally treated as symbol stops and needs comparison with the original reel implementation.

Graphic lamps use `brightmask_main` for the off image and `sublamp_1_main_image` for the on image. In this first pass the whole component switches state. Compound lamps preserve their decoded lamp-number tables, but individual sublamp masks, alternate on images and specialized Blend/inversion/effect rules are not applied. Colour/shape/text-only lamps remain outlines until implemented. The supplied Party Time sample has artwork for all 99 Lamp components and band images for all six reels.

## Rendering test

After loading a layout, choose **UI reconstruction → Animate reels and lamps (test)**. All rendered reels spin continuously at slightly different speeds, and all rendered graphic lamp components switch on/off every half second. Reel illumination switches with them. The menu is checked while running; click it again to stop. Idle means reel position zero (plus its saved band offset), with lamp outputs off.

The test starts disabled and never starts automatically. It stops when a replacement layout loads successfully, when entering Game Manager, or when closing the main window. A failed load preserves the current view. No state is written to the FML or game configuration, no ROM runs, and no gameplay or machine timing is simulated.

A monotonic clock drives the demonstration through a UI timer. Actual emulated timing must later come from an independent engine. The renderer’s state setters consume symbol positions and lamp on/off states, so drawing does not depend on the demonstration timer. Images and transparency conversions are prepared once per load, not decoded on each animation frame.

Synthetic pixel checks verify colour-key/alpha handling, gradient transmission, lamp artwork changes, symbol advancement, fractional positions, negative/end-of-band wrapping, horizontal/reversed strips and resetting idle output. Real Party Time checks verify all six reel and 99 lamp controls, menu behavior, actual timer ticks and stopping on hide/reload/close. Neither copyrighted game assets nor ROMs are included in the repository.

Display glyphs, specialized transparency/tiling, hardware callbacks and emulation remain to be implemented. Labels use approximate font rendering. Components are painted in decoded file order; this does not yet reproduce every MFME custom-control painting rule.

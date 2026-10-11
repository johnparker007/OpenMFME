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

This is a static layout preview. Reels, lamps, buttons, checkboxes and displays retain their geometry and decoded properties, but currently render as outlines rather than functional machine controls. Saved backgrounds may already show their cabinet artwork.

Reel-band rendering, display glyphs, lamp masks/blending, specialized transparency and tiling, hardware callbacks and emulation remain to be implemented. Labels use approximate font rendering. Components are painted in decoded file order; this does not yet reproduce every MFME custom-control painting rule. The inspector provides the data needed for those next rendering steps.

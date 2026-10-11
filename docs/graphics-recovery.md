# Embedded graphics recovery

The same MFME.exe source hash recorded in the form catalog yielded 44 binary image references: 41 button glyphs, one original application icon and two Windows image-list containers. The 41 glyph references share 14 distinct BMP files. Files are named by the SHA-256 of their original DFM payload; the manifest records their form, component, property, source range, dimensions and frame count.

## Rendered graphics

The extractor validates each glyph's four-byte VCL length prefix and uncompressed BMP header, then stores the BMP without its prefix. The app decodes it through Avalonia, crops horizontal state strips according to the saved NumGlyphs property, and makes the bottom-left colour transparent. Frame one is the normal image and frame two, where supplied, is the disabled image. Preview commands remain disabled, so their disabled glyph is normally shown. Oversized glyphs shrink to fit their button.

The packaged glyphs do not require the original executable at runtime. Smoke checks reconstruct their VCL wrapper and verify each hash against the catalog, then check both normal and disabled frame dimensions and transparent background pixels.

## Archived containers

The original ICO and two image-list containers are preserved unchanged, with source hashes. The original icon is not used as OpenMFME branding. Image-list colour/mask decoding and associations with runtime menu/list entries remain unresolved; these files are not rendered yet.

## Custom controls

TManyShape palette controls show their saved background and outline colours. This implements the observed swatches, not the component's full gradient, shadow or vector features. TImage, TImage32, TImgView32 and TPaintBox32 use clean empty canvases: none of these controls has an embedded image payload in the recovered forms. Runtime artwork and drawing tools remain unimplemented.

TzcLightOnOff controls occur as 20 DIP switches. Their switch appearance is a new approximation because the saved forms contain geometry but no drawing code or switch-state artwork. They do not send machine inputs or persist settings. This visual must be verified against the original control before claiming fidelity.

Fonts, slider sizing, docking, inherited defaults and runtime-populated content still need a later fidelity pass. Original event-handler implementations have not been recovered by this work.

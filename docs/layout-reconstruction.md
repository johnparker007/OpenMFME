# Saved layout and font reconstruction

Form previews now use a dedicated layout panel rather than a fixed canvas. Positions remain in their original logical coordinates; enlarging a preview does not scale every control.

## Fonts and keyboard order

Font name, absolute saved Font.Height, bold/italic style and colour resolve through the component hierarchy. Missing font properties inherit from the parent. ParentFont=false starts from a neutral default before applying the control's own saved properties. The Windows bitmap-font name MS Sans Serif maps to Microsoft Sans Serif; this substitution and native text metrics can differ from the original executable. Underline/strikeout and Delphi DPI/font-height conversion nuances are not yet implemented.

The main meter strip receives its original form's font context. Container captions, radio groups and tab headers inherit the resolved font rather than applying unrelated defaults. TabOrder maps to tab index within a local navigation scope; TabStop=false excludes a control from focus. Disabled preview commands remain excluded by normal Avalonia navigation.

The ParentFont convention follows [Embarcadero's description](https://docwiki.embarcadero.com/Libraries/Sydney/en/Vcl.Controls.TControl.ParentFont). This is newly implemented rendering, not recovered Delphi code.

## Layout

The mapper supports alTop, alBottom, alLeft, alRight and alClient. Edge controls consume the available rectangle; client controls fill what remains. Saved coordinates order controls within each docking edge, and hidden controls consume no space. Status bars use their usual bottom alignment when the stream omits Align. The edge-order implementation is an approximation and has not been compared against every original VCL layout case.

Undocked controls retain their saved bounds. Right/bottom anchors move controls with the corresponding edge; opposite anchors stretch them. An axis with neither anchor shifts by half the parent's size change. Calculations always start from saved geometry to avoid drift across repeated resizes.

Tab controls with a saved client child reserve the inset derived from that child's saved bounds. This keeps tab headers above their contents. Page controls still use Avalonia's page layout, so their client insets can differ from VCL. Runtime-created tabs and technology-specific visibility changes are not inferred from this data.

The preview's initial size includes the measured warning, footer and optional menu around the saved surface. Saved fixed/dialog border styles disable user resizing. Resizable previews expand their surfaces; shrinking below the saved surface uses scrolling. Original minimum/maximum constraints, native borders, AlignWithMargins, splitter dragging and complete VCL client-area calculations remain future work.

## Compact controls

TScrollBar has a scrollbar with arrow/page buttons and a draggable thumb; it is no longer drawn as a slider. TTrackBar uses a compact slider thumb and integer steps. Common scroll values, page size, change increments and orientation are preserved. Scrollbars use saved Kind for orientation. Arrow clicks, thumb dragging and keyboard input alter temporary preview values only. Standalone scrollbars provide arrows, Page Up/Down and Home/End keys; trackbar increments use LineSize/PageSize rather than the visual tick Frequency. Tick marks, all original keyboard behaviours and application event handlers are not implemented.

Tab headers use compact templates so the selected-tab indicator no longer crosses large saved captions. The saved-only TTabControl header strip does not introduce a second content pane.

## Checks

Synthetic cases verify edge docking, hidden-control exclusion, edge/opposite-edge/no anchors, returning to the original size, nested fonts, independent fonts and tab navigation despite reversed component order. Real form checks verify that the Graphic Editor's canvas grows while its lower toolbar remains docked, scrollbar arrows/keyboard input work, and both range controls support thumb dragging. The Preferences slider responds to the keyboard with its thumb inside the saved bounds. All 66 form previews continue to render headlessly.

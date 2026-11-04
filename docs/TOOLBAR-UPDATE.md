# Compact toolbar and stable pen strokes — v0.3.1-preview.1

Requested from the user's three screenshots: stop stroke reshaping on release, remove repeated actions, start with a slim icon toolbar, keep expanded controls available, show current shortcuts on hover, add an app icon and push the update.

- Disable WPF FitToCurve for new permanent and temporary strokes. Preserve raw pen/finger corners through completion, serialization and undo/redo. Existing saved stroke attributes are preserved.
- Start every launch at a 60-DIP icon rail, including installations with an old expanded preference. Expand settings is session-only. Flyouts contain drawing tools, colour/width and screenshot choices; the expanded panel holds lesson/page/input/settings actions without duplicate Pen/Laser/Board/Save buttons.
- Hover tips are generated from active bindings and update on remap. Disabled global keys are labelled not assigned; local Ctrl shortcuts are labelled toolbar focused. Vector glyphs and accessible control names support scaled displays and keyboard use.
- Compile a native 16–256px pen-nib icon into the Windows executable and embed matching toolbar/tray assets. Keep the toolbar within the nearest monitor work area after expansion.
- Escape, collapsing settings, hiding the toolbar or entering a modal action closes flyouts.

Validation: cross-build plus core tests locally; Windows adapters cover raw corners/history, compact upgrade defaults, duplicate action detection, remapped hover tips, embedded icon and real WPF renders of compact/expanded layouts. Physical pen lift and desktop interaction still need confirmation on the user's device.


Windows verification completed: [run 41811137768](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41811137768), all 73 cases passed with 0 build warnings/errors. The tests show the real toolbar window, exercise expansion/scrolling and save WPF render evidence. [Download v0.3.1](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41811137768/artifacts/13312676497).

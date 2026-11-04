# Version 3 — screen annotation additions

The request was to add the functionality missing in the Epic Pen comparison. This preview adds:

| Area | Implementation |
| --- | --- |
| Colour | 24 swatches and a native custom RGB picker for ink and text |
| Shortcuts | Configurable Ctrl+Alt+letter/digit/F1–F24 for every tool/action; transactional conflict rollback; V2 recovery-key migration |
| Ghost Mode | Hide only the toolbar; keep drawing/ink; restore by hotkey or tray; emergency Hide remains separate |
| Tool sizes | Width stored independently for each tool and persisted |
| Screenshot | Combined-desktop PNG/clipboard, frozen region selection, reverse-drag/DPI coordinate conversion, Save/Copy/Cancel |
| Displays | One overlay across the virtual desktop, negative monitor origins, display/DPI-change repositioning and toolbar recovery |
| Pen/touch | Pressure option; independent simultaneous finger ink with separate undo; proximity-based touch suppression |

Existing .sketchora lessons keep schema version 1 and remain readable. Settings gain optional fields; old settings retain Draw/Hide keys. Ghost Mode is not persisted. No accounts, paid licensing or cloud service is added.

## Verification scope

Core regression tests cover preference migration/persistence, shortcut validation, region clipping/scaling, and canvas resizing without losing history. Windows adapter tests cover independent widths, pressure attributes, multiple concurrent touch stroke sessions, touch cancellation, transient exclusion, bitmap lifetime and region-window construction. These are automated software checks, not physical digitizer or receiver-side meeting validation.

Before declaring full feature parity, complete the manual checks below and in WINDOWS-CHECKLIST.md. In particular, mixed-DPI displays use one spanning WPF coordinate transform; alignment across hardware must be measured, and per-monitor windows may be needed if the device's DPI configuration exposes errors. Palm suppression requires an active pen's proximity events. Shapes accept one touch; simultaneous input applies to freehand pen/highlighter/fading ink.

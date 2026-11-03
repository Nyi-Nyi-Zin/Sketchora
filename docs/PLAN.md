# Sketchora — Teaching Annotation Desktop App Plan

Updated: 4 November 2026. Product name selected for this project: Sketchora.
Status: first implementation added in source. Build/test results are recorded in VALIDATION.md; Windows UI and device validation remain pending.

## 1. Goal and assumptions

Build an offline Windows desktop tool for online teachers to point at content, annotate code/PDFs/websites, and sketch explanations while sharing their screen.

- First release: Windows 11, mouse and touchscreen; compatible digital pen where available.
- First supported configuration: one selected display. Test a two-display setup before release; simultaneous drawing across displays is later scope.
- Target workflow: teacher presents in Zoom or Google Meet, draws an explanation, returns to the underlying application, then saves a screenshot if useful.
- No account, server, subscription or internet connection is required for the drawing functions.
- Product name: Sketchora. Visual identity will use a simple pen/teaching motif. Public distribution requires a separate name availability check; this plan does not establish trademark or domain availability.

## 2. Technology decision

Selected stack: C# + WPF on .NET 10 LTS, using the latest supported stable patch available at implementation time. Use InkCanvas for initial ink input, WPF shapes for geometry, and a small Win32 interop layer for window behavior and global shortcuts.

This is a project recommendation for a Windows-first tool, not a verified performance comparison. WPF supplies ink primitives, while Windows exposes layered-window and mouse pass-through behavior. Device input, touch routing, focus and capture still require a working prototype.

Use MVVM with CommunityToolkit.Mvvm to separate toolbar/settings views, application state and commands. Keep overlay-specific native calls behind focused interfaces. This is a Windows-first implementation; cross-platform UI is outside the selected architecture.

| Concern | Selected approach |
| --- | --- |
| Runtime and UI | C#, .NET 10 LTS, WPF/XAML |
| Input and rendering | InkCanvas, retained annotation data, WPF shapes |
| Window integration | Small Win32 interop layer for overlay/focus/input/hotkeys |
| Application architecture | MVVM with CommunityToolkit.Mvvm |
| Local preferences | System.Text.Json, per-user application data, atomic writes |
| Export | PNG with background and annotation composition; toolbar excluded |
| Release | Self-contained Windows x64 package and installer; distribution/signing decision before public release |

Selection principle: prioritize reliable input, recoverable mode switching and maintainability. Add a dependency only when it resolves a concrete requirement. Do not promise low resource usage or superior latency without measurements on the target laptop.

Local storage: JSON preferences and a versioned JSON session format. Export: PNG. A database is unnecessary initially.

## 3. Interaction model

| Mode | Pointer behavior | Drawing behavior |
| --- | --- | --- |
| Interact | Click, scroll and type in the application below | Existing marks can remain visible; no new marks |
| Draw | Overlay receives drawing input | Pen, highlight, shapes or erase |
| Whiteboard | Board receives input | Draw on a solid white/black background |

- Keep a small movable toolbar in a separate window so it stays usable when the overlay passes input through.
- Show the active mode with a label and icon, not color alone.
- A configurable global shortcut switches Draw/Interact. A recovery shortcut and tray action hide the overlay and release input.
- Do not capture ordinary typing shortcuts globally. Undo/redo should affect annotations only when our drawing surface has the appropriate focus/mode.
- Desktop marks are attached to screen coordinates, not a document or code line. When the page scrolls, marks do not follow its content. Provide quick Hide/Clear actions.
- Changing to whiteboard preserves desktop marks separately. Returning restores the previous mode and layer.
- Hide marks without deleting them. Clear is undoable; exiting an unsaved session offers Save/Discard/Cancel.

## 4. MVP scope

| Feature | Expected behavior |
| --- | --- |
| Floating toolbar | Move, collapse and recover from tray; remains within display bounds |
| Pen | Freehand strokes with color and width controls |
| Highlighter | Translucent strokes that keep underlying text readable |
| Eraser | Whole-stroke/object erasure initially |
| Undo/redo | Includes adding, erasing and clearing annotations |
| Shapes | Straight line, arrow, rectangle and ellipse |
| Pointer highlight | Visible halo while presenting; configurable on/off |
| White/black board | One board initially; separate from desktop annotations |
| Hide/Clear | Hide preserves work; Clear can be undone |
| Input modes | Safe, explicit Draw/Interact switching |
| PNG export | Save selected display with annotations; omit toolbar |
| Preferences | Remember tool color, width, shortcuts and toolbar location |
| Packaging | Install, launch, tray menu, exit and uninstall |

Mouse is a baseline supported input. Touch is a release test requirement. Pressure sensitivity and palm rejection are conditional on compatible device/driver capabilities and should not be advertised until tested.

## 5. After MVP

Version 1.1: timed fading pen, text labels including Burmese input/font testing, board pages, editable session save/load and crash recovery, configurable shortcut UI.

Later: simultaneous multi-monitor overlays, advanced spotlight dimming, selection/move/resize, PDF/image import, SVG export, improved pen features and signed automatic updates.

Separate future projects: macOS/Linux ports, recording, cloud sync, student collaboration, subscriptions and licensing. These are outside the first release estimate.

## 6. Component responsibilities

| Component | Responsibility |
| --- | --- |
| App shell/tray | Single instance, startup/shutdown, recover hidden UI |
| Toolbar/settings | Tool choice, visible mode, preferences and accessible controls |
| Overlay controller | Topmost windows, focus, pass-through, selected display, DPI changes |
| Annotation engine | Stroke/shape data, rendering, undo/redo, separate board/desktop layers |
| Input adapter | Mouse/touch/stylus normalization; avoid duplicate pointer strokes |
| Export/storage | PNG capture, local settings, session format and safe file writes |

Store geometry in logical coordinates with explicit display/scale metadata. Keep application data independent of the view so sessions can be migrated and exported later.

## 7. Milestones and rough effort

Assumption: one developer working full time. These are indicative human development effort estimates, not a promised assistant delivery date. Estimates include normal debugging but exclude broad platform support and advanced features. Hardware-specific issues can extend the schedule. The assistant will implement the code from scratch; native Windows execution, input and screen-share checks need a suitable Windows test environment.

| Milestone | Rough effort | Completion evidence |
| --- | --- | --- |
| A. Feasibility prototype | 3–5 working days | Transparent overlay, one pen, accessible toolbar, mode toggle, usable underlying browser and VS Code, successful receiver-side display sharing |
| B. Drawing tools | 5–7 days | Pen/highlighter/eraser, shapes, colors/widths and undo/redo work together |
| C. Teaching workflow | 4–6 days | Pointer halo, whiteboard, hide/clear, PNG export and preferences |
| D. Reliability and release | 5–8 days | DPI/input/capture tests, long session, installer and documented limitations |

Indicative total: 17–26 working days, approximately 4–6 weeks. Re-estimate after milestone A. Version 1.1 is a separate estimate.

Milestone A is a gate: do not promise reliable lesson use or implement the full feature set until mode switching and shared-screen visibility work on the target machine.

## 8. Release checks

- In Interact mode, click, scroll and type in browser/VS Code while marks remain visible. The overlay must not trap input or unexpectedly steal typing focus.
- In Draw mode, strokes stay under the pointer; toolbar presses do not draw behind the toolbar. Verify mouse, actual touchscreen and any available supported pen separately.
- If pen-only mode is added, verify touch does not introduce duplicate strokes; palm rejection is a device-specific test.
- On the actual teaching setup, use a second participant device to verify marks and pointer highlight in Zoom and Google Meet full-display sharing and any lesson recording. A local preview alone is insufficient. Individual-window sharing may omit the overlay; document tested modes.
- Test 100%, 125%, 150% and 200% display scaling, primary/secondary display selection, disconnect/reconnect, negative display coordinates and sleep/wake.
- Test normal presentation/full-screen app behavior. Elevated apps, secure desktop and exclusive full-screen applications are outside initial guarantees; recovery must remain safe.
- Verify undo/redo across strokes, shapes, erase and clear; a new edit after undo invalidates redo as expected.
- Confirm PNG export contains background plus marks at the correct scale and excludes toolbar/export dialogs. Handle canceled save and unwritable paths without losing annotations.
- Run a 60-minute simulated lesson with real screen sharing; inspect input responsiveness and memory growth on the target laptop. Establish measured budgets after the feasibility prototype.
- Test keyboard access, visible focus, descriptive tool names/tooltips and readable selected-state indicators. Touch controls should be easy to press without activating adjacent tools.
- Verify a shortcut conflict is reported and a recovery tray action remains available. Reset invalid stored toolbar positions after display changes.
- Install, start, exit and uninstall on a clean Windows account. Decide signing/distribution before public release; signing credentials and cost are not assumed.

## 9. First implementation task

Create a minimal Windows project with a transparent topmost drawing overlay, a separate toolbar with Pen/Interact/Clear/Exit, and a recovery hotkey. Validate it on the teaching laptop while sharing the whole display. Only after that check passes should shapes, whiteboard and export be added.

## Primary references

- Microsoft WPF ink overview: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/getting-started-with-ink
- Microsoft layered-window and input behavior: https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features
- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
- MVVM Toolkit: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/

References establish available APIs, not proof that the planned product already works. Screen sharing, touch and performance claims require the release checks above.

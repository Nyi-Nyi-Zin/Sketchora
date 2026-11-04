# Sketchora

A Windows desktop app for annotating code, drawing sketches and explaining lessons while sharing your display.

**Version 3 update: v0.3.1-preview.1.** Adds 24 colours/custom RGB, configurable per-tool/action hotkeys, Ghost Mode, per-tool widths, region/clipboard screenshots, a virtual-desktop canvas, simultaneous finger ink and pen input controls. This is a development preview. Actual pen/touch hardware, display scaling and receiver-side Zoom/Meet capture still need manual validation; automated build/tests are not a substitute.

## Download or build

**[Download Version 3 Windows preview](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41805713686/artifacts/13309618067)** · [Passing Windows build and all 67 tests](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41805713686)

Includes the concurrent-touch fix. The earlier V2 artifact does not contain these additions.

Open the latest successful run of [Build and test Sketchora](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/workflows/build.yml), then download the **Sketchora-win-x64** artifact. GitHub may require sign-in. Extract the ZIP and open `Sketchora.exe`, keeping the extracted files together. It is a self-contained, unsigned Windows 11 x64 portable app, not an installer. Actions artifacts expire; use a current successful run.

To build from source with the .NET 10 SDK:

```powershell
dotnet restore Sketchora.sln
dotnet run --project src/Sketchora.App/Sketchora.App.csproj
```

## Drawing and pointing

- The app starts in **Interact** mode so you can click and type in the application underneath.
- Choose **Start drawing** or a drawing tool to annotate. Choose **Use desktop** to resume normal app input.
- Pen, highlighter, whole-stroke eraser, line, arrow, rectangle and ellipse are available with canvas-specific undo/redo.
- Choose from **24 quick colours** or **Custom colour…** for a native RGB picker. Text labels have the same palette/picker.
- Each tool remembers its own width across switches and restarts. Highlighter width is displayed at four times the base width.
- **Pen pressure** controls variable-width pen input. **Touch drawing** accepts independent simultaneous finger strokes; each completed stroke is independently undoable. Shapes use one touch at a time.
- **Ignore touch near pen** discards pending finger ink and blocks touches while a pen is in digitizer range. This is a proximity guard, not a universal hardware palm classifier. Passive capacitive pens report as fingers and have no pressure/proximity signal.
- **Fading / Laser** strokes expire 1, 3 or 5 seconds after stroke completion. They are separate from permanent ink and are excluded from editable lessons, autosave, permanent undo/redo and PNG exports.
- **Clear laser** removes temporary marks. **Clear** removes permanent ink and text on the active canvas and can be undone.
- **Show ink** hides the annotations without deleting them. **Pointer halo** makes the mouse easier to follow.
- Desktop ink stays fixed to screen coordinates when the underlying website/editor scrolls. Hide or clear marks before moving to other content.

## Editable lessons and recovery

Use **New / Open / Save / Save as** for `.sketchora` lessons. These files preserve desktop ink, board pages, text labels, page backgrounds, canvas dimensions and selection. PNG is a separate flattened export.

- Save/Open do not embed screenshots of other apps or the desktop.
- Failed or invalid file loads keep your current lesson intact. New/Open/Quit offers **Save / Discard / Cancel** when edits are unsaved.
- If a saved canvas has another display size, choose **Fit** or **Original positions**. Original-position content outside the current display may be clipped.
- Local recovery is written after a short pause in editing, or at least every 30 seconds during continuous edits, while the document is dirty. A failed recovery write reports an error and retries with a backoff.
- At restart, choose whether to restore or discard the recovery copy. Recovered work uses **Save as** so the original named lesson is preserved.
- Files are limited to 32 MiB, 100 board pages, 1,000 text labels per canvas and 10,000 characters per label. Ink encoding/point limits and schema validation are enforced.

## Board pages

The **Board** button switches between desktop annotations and the selected board. **White / black page background** changes that page's background without clearing its contents.

Use **Previous / Next / Add page / Rename / Copy / Delete / Restore** to manage boards. Each page has an independent history. Copy is available while viewing a board. The final board cannot be deleted. Restore recovers deleted boards within the current session; deleted-page history is not stored in lesson files.

## Text labels

Choose **Text**, then click an empty canvas location. The focused editor supports multi-line English/Burmese Unicode text, font size and color. **Enter** inserts a new line; use **Done** or **Cancel** explicitly. After editing, the previous drawing tool is restored.

To edit existing labels, select Text again, then **drag to move** or **double-click to edit/delete**. Text edits and moves are undoable. Fonts use local Myanmar Text/Segoe UI fallback; real Burmese input-method and glyph-rendering checks remain pending.

## Screenshots

**Save PNG** captures the combined Windows desktop, including annotations. **Copy screenshot** copies it to the clipboard. **Capture region…** shows a frozen desktop: drag in either direction, then choose **Save PNG** or **Copy**. Cancel/Escape leaves the lesson untouched. The screenshot is taken before the selection UI and save dialog appear; temporary ink, halo and toolbar are excluded. Display-layout changes cancel an in-progress region selection. Capture is limited to 100 million pixels.

## Toolbar, shortcuts and recovery

The toolbar can be moved, collapsed and recovered through the tray. **Ghost Mode** hides only the toolbar while ink and drawing remain active. Its shortcut restores it; **Show toolbar** in the tray also recovers it. **Hide all** remains a separate emergency action that hides ink and releases input. Ghost Mode is session-only and never hides the toolbar on startup. Every launch starts as a **60-DIP icon rail**, including upgrades from saved expanded layouts. It contains desktop interaction, ink visibility, a drawing-tool menu, eraser, colour/width menu, undo/redo, clear, screenshot menu, clipboard copy and board switching. **Expand settings** opens lesson/page/input controls beside the rail; each action appears once. Expansion is session-only.

Hover a control to see its name and the **currently assigned** global shortcut. Tips update when shortcuts change, distinguish toolbar-focused Ctrl shortcuts, and say when no global key is assigned. The app has a custom pen-nib icon in the executable, toolbar and system tray.

New pen/highlighter/fading strokes use the captured path directly with **curve fitting disabled**. This prevents the post-lift Bézier reshaping previously visible when writing or drawing corners. Existing authored strokes retain their stored attributes.

All global bindings use **Ctrl + Alt**. Defaults:

| Key | Action |
| --- | --- |
| D / H / G | Draw–interact / emergency Hide–Show / Ghost Mode |
| P / M / E | Pen / Highlighter / Eraser |
| L / A / R / O | Line / Arrow / Rectangle / Ellipse |
| T / F | Text / Fading ink |
| Z / Y / C | Undo / Redo / Clear |
| S / B / X | Save PNG / Copy screenshot / Capture region |
| W | Desktop–board switch |

**Shortcut settings** lists every tool/action, including lesson/page actions and input/width/colour toggles. Choose letters, digits or F1–F24; optional actions can be Off. Draw, Hide and Ghost must stay enabled with distinct keys. Conflicts roll back the entire change and retain previous working bindings. **Defaults** resets the form before Apply. Existing V2 Draw/Hide choices migrate; colliding new defaults use free function keys. If startup registration conflicts, the app attempts recovery-only bindings and reports this; the tray stays available.

Ctrl+S / Ctrl+Shift+S, Ctrl+O / Ctrl+N and Ctrl+Z / Ctrl+Y also work while the toolbar has focus. They do not intercept another application's normal Ctrl shortcuts. Closing the toolbar hides Sketchora; use Quit to exit.

## Screen sharing and limitations

Use **entire display sharing** in Zoom/Meet. Individual-window sharing may omit the annotation overlay. Verify from a second participant device and in lesson recordings.

The canvas and screenshots now span the **combined Windows virtual desktop**, including monitors left of or above the primary display. On display/DPI changes the canvas bounds update, unfinished touch ink is cancelled and the toolbar is recovered to the primary work area. Authored positions are preserved relative to the canvas origin; check alignment after rearranging displays. A spanning WPF window has a single coordinate/DPI transform: mixed-DPI accuracy, hot-plug behavior, actual touch/pen devices, pressure/proximity filtering and meeting capture still need the [manual checklist](docs/WINDOWS-CHECKLIST.md). Secure desktop, elevated apps and exclusive full-screen apps are outside the initial support target.

General object rotation/resize, PDF, recording, cloud sync, collaboration, installers, automatic updates and macOS/Linux are not included. No account, backend or app analytics is used.

Preferences and recovery are local under `%LOCALAPPDATA%\Sketchora`. Unexpected UI errors release the screen and log to `error.log` there.

## Build and test

```powershell
dotnet build Sketchora.sln -c Release
dotnet test Sketchora.sln -c Release --no-build
dotnet publish src/Sketchora.App/Sketchora.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/Sketchora-win-x64
```

The full suite requires Windows because it includes WPF adapters and hidden-window construction on STA threads. On Linux, cross-build the solution and run `dotnet test tests/Sketchora.Core.Tests/Sketchora.Core.Tests.csproj` for core behavior. GitHub Actions runs the complete suite and publishes the portable app.

| Project | Responsibility |
| --- | --- |
| `src/Sketchora.Core` | Lesson/schema validation, atomic storage, page workspace, histories, save guards, temporary-ink clock and transactional shortcut binding |
| `src/Sketchora.App` | WPF UI/ink serialization, text editor, autosave/recovery, Win32 overlay, shortcuts, tray and capture |
| `tests/Sketchora.Core.Tests` | Storage, history, page isolation, failure paths, expiry, save choices and shortcut rollback |
| `tests/Sketchora.App.Tests` | Windows ink/pressure round-trip, fitting, UI construction and transient/permanent separation |

See [Version 3 additions](docs/VERSION-3-FEATURES.md), [Version 2 plan](docs/VERSION-2-PLAN.md), [validation record](docs/VALIDATION.md) and [Windows checklist](docs/WINDOWS-CHECKLIST.md).

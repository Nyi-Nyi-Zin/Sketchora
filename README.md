# Sketchora

A Windows desktop app for annotating code, drawing sketches and explaining lessons while sharing your display.

**Version 2: v0.2.0-preview.1.** Editable lessons, board pages, text and fading ink are implemented. This is a development preview. Actual pen/touch hardware, display scaling and receiver-side Zoom/Meet capture still need manual validation; automated build/tests are not a substitute.

## Download or build

**[Download Version 2 Windows preview](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41744279619/artifacts/13282534262)** · [Verified Windows build and tests](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41744279619)

Open the latest successful run of [Build and test Sketchora](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/workflows/build.yml), then download the **Sketchora-win-x64** artifact. GitHub may require sign-in. Extract the ZIP and open `Sketchora.exe`, keeping the extracted files together. It is a self-contained, unsigned Windows 11 x64 portable app, not an installer. Actions artifacts expire; use a current successful run.

To build from source with the .NET 10 SDK:

```powershell
dotnet restore Sketchora.sln
dotnet run --project src/Sketchora.App/Sketchora.App.csproj
```

## Drawing and pointing

- The app starts in **Interact** mode so you can click and type in the application underneath.
- Choose **Start drawing** or a drawing tool to annotate. Choose **Use desktop** to resume normal app input.
- Pen, highlighter, whole-stroke eraser, line, arrow, rectangle and ellipse are available with color/width controls and canvas-specific undo/redo.
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

## Toolbar, shortcuts and recovery

The toolbar can be moved, collapsed and recovered through the tray. The compact view retains Pen, Laser, Board and Save actions and shows the current tool/mode.

| Shortcut | Default action |
| --- | --- |
| Ctrl + Alt + D | Show app and toggle Draw/Interact |
| Ctrl + Alt + H | Hide all and release screen input; press again to show |
| Ctrl + S / Ctrl + Shift + S | Save / Save as while toolbar has focus |
| Ctrl + O / Ctrl + N | Open / New while toolbar has focus |
| Ctrl + Z / Ctrl + Y | Undo / Redo while toolbar has focus |

**Shortcut settings** lets you choose two different Ctrl+Alt+letter bindings. Registration conflicts leave the previous bindings intact. The tray recovery menu is independent of global shortcuts. Emergency Hide also cancels the text editor safely. Closing the toolbar hides Sketchora; use Quit to exit.

## Screen sharing and limitations

Use **entire display sharing** in Zoom/Meet. Individual-window sharing may omit the annotation overlay. Verify from a second participant device and in lesson recordings.

The initial support target is the primary display on Windows 11 x64. Mixed-DPI/hot-plug behavior, actual touch/pen devices, pressure/palm rejection and meeting capture need the [manual checklist](docs/WINDOWS-CHECKLIST.md). Secure desktop, elevated apps and exclusive full-screen apps are outside the initial support target.

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

See [Version 2 plan](docs/VERSION-2-PLAN.md), [validation record](docs/VALIDATION.md) and [Windows checklist](docs/WINDOWS-CHECKLIST.md).

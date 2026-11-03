# Sketchora

A Windows desktop annotation app for explaining code, drawing sketches and pointing out details during online lessons.

**Status: early development, v0.1.0.** The source includes the first teaching workflow. Windows interaction, pen hardware and receiver-side Zoom/Meet capture require manual validation before teaching a live class. This is not an installer release.

## Implemented in source

- Transparent topmost drawing overlay on the primary display.
- Explicit Draw/Interact modes; mouse input passes through to other apps in Interact mode.
- Movable, collapsible toolbar with a tray recovery menu.
- Pen, translucent highlighter, whole-stroke eraser, line, arrow, rectangle and ellipse.
- Color and width controls, undo/redo and undoable Clear.
- Separate desktop and whiteboard ink; white and black board backgrounds.
- Optional pointer halo, show/hide ink and global hide/recovery shortcut.
- PNG screen capture with toolbar and halo temporarily hidden.
- Local preferences, single-instance handling and unsaved-ink exit confirmation.

## Requirements

- Windows 11 x64 is the initial target.
- .NET 10 SDK to build/run from source. A self-contained publish includes the runtime.
- Mouse, touchscreen or compatible pen. Pressure and palm rejection depend on device/driver support and are not yet certified.

## Download the Windows preview

[Download Sketchora-win-x64](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41740441761/artifacts/13279444646) from the successful [Windows build](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41740441761). GitHub may require sign-in to download an Actions artifact. Extract the ZIP and open `Sketchora.exe`; keep the extracted files together. This is a portable development preview, not an installer or a completed Windows usability test. Actions artifacts expire; newer builds appear in the repository's Actions tab.

## Run

From a terminal at the repository root on Windows:

```powershell
dotnet restore Sketchora.sln
dotnet run --project src/Sketchora.App/Sketchora.App.csproj
```

The app starts in **Interact** mode. Select **Start drawing** or a drawing tool to draw over your screen. Select **Use desktop** before clicking or typing in the app underneath.

| Shortcut | Action |
| --- | --- |
| Ctrl + Alt + D | Show app and toggle Draw/Interact |
| Ctrl + Alt + H | Hide everything and release input; press again to show |
| Ctrl + Z | Undo while the toolbar has keyboard focus |
| Ctrl + Y | Redo while the toolbar has keyboard focus |

If a global shortcut is already registered by another app, Sketchora reports a conflict. The tray menu remains the fallback. Closing the toolbar hides Sketchora; use **Quit** in the toolbar or tray to exit.

## Build and test

```powershell
dotnet build Sketchora.sln -c Release
dotnet test tests/Sketchora.Core.Tests/Sketchora.Core.Tests.csproj -c Release
dotnet publish src/Sketchora.App/Sketchora.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/Sketchora-win-x64
```

GitHub Actions is configured to build on Windows, run core tests and upload a portable Windows artifact. A workflow file being present does not mean a CI run has executed or passed. Review [docs/VALIDATION.md](docs/VALIDATION.md) for the checks actually completed.

## Using screen sharing

Start with **entire display sharing** in Zoom/Meet. Individual-window sharing can omit the overlay because the ink is a separate window. Verify from a second participant device, including recordings, before using it in a lesson.

Desktop ink is fixed to the screen; it does not follow a web page or editor content when you scroll. Hide or clear it before moving to a different explanation. Selecting the board button cycles desktop → whiteboard → blackboard → desktop. Whiteboard and blackboard share one board's strokes; desktop strokes are separate.

## Data and limitations

- Preferences: `%LOCALAPPDATA%\Sketchora\preferences.json`.
- Unexpected UI errors release the overlay and log locally to `%LOCALAPPDATA%\Sketchora\error.log` before closing.
- No account, backend, cloud sync or app analytics is included.
- PNG exports are local files containing the visible primary display. Review other visible content before sharing them.
- Editable sessions, fading pen, text labels, multi-page boards and simultaneous multi-monitor drawing are planned.
- Exiting discards ink after confirmation. PNG is a flattened image, not an editable session.
- Initially targets the primary display. Mixed-DPI, hot-plug, pen and screen-capture behavior are pending Windows tests.
- Secure desktop, elevated applications and exclusive full-screen applications are outside the initial support target.
- Portable builds are unsigned; code signing and an installer are future release work.

## Structure

| Project | Responsibility |
| --- | --- |
| `src/Sketchora.Core` | Bounded snapshot history, normalized preferences and atomic persistence |
| `src/Sketchora.App` | WPF toolbar, ink surfaces, Win32 overlay, shortcuts, tray and export |
| `tests/Sketchora.Core.Tests` | History branching/isolation/capacity and preferences recovery/round-trip tests |

Uses C#, .NET 10 LTS, WPF and CommunityToolkit.Mvvm. See [docs/PLAN.md](docs/PLAN.md) and [docs/WINDOWS-CHECKLIST.md](docs/WINDOWS-CHECKLIST.md).

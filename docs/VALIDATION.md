# Validation record

## Completed on 4 November 2026

- .NET SDK 10.0.403 on Linux, cross-targeting the Windows project.
- `dotnet build Sketchora.sln -c Release --no-restore --nologo -m:1 -nr:false`: passed, 0 warnings, 0 errors.
- `dotnet test tests/Sketchora.Core.Tests/Sketchora.Core.Tests.csproj -c Release --no-build`: passed, 10 tests, 0 failed, 0 skipped.
- Tests cover undo/redo, redo branching, defensive copy isolation, capacity boundaries, undoable Clear, failed-snapshot recovery, first-launch settings, malformed settings, normalization and atomic file replacement/round-trip.
- The build validates C# and WPF XAML compilation. It does not establish native window/input correctness.
- WinForms DPI analyzer WFO0003 is specifically suppressed with an explanation in the app project: WPF owns startup and PerMonitorV2 awareness via the manifest; WinForms is used for tray/capture only.

## Pending

- Native Windows UI execution and visual inspection.
- Touch/pen input, DPI and capture behavior.
- Receiver-side Zoom/Google Meet sharing and recordings.
- GitHub Actions execution (requires the repository to exist and code to be pushed).

See WINDOWS-CHECKLIST.md for the manual release gate. A successful cross-build does not validate desktop interaction.

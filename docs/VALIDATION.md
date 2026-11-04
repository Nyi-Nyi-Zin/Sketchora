# Validation record

## Completed on 4 November 2026

- .NET SDK 10.0.403 on Linux, cross-targeting the Windows project.
- `dotnet build Sketchora.sln -c Release --no-restore --nologo -m:1 -nr:false`: passed, 0 warnings, 0 errors.
- `dotnet test tests/Sketchora.Core.Tests/Sketchora.Core.Tests.csproj -c Release --no-build`: passed, 10 tests, 0 failed, 0 skipped.
- Tests cover undo/redo, redo branching, defensive copy isolation, capacity boundaries, undoable Clear, failed-snapshot recovery, first-launch settings, malformed settings, normalization and atomic file replacement/round-trip.
- The build validates C# and WPF XAML compilation. It does not establish native window/input correctness.
- WinForms DPI analyzer WFO0003 is specifically suppressed with an explanation in the app project: WPF owns startup and PerMonitorV2 awareness via the manifest; WinForms is used for tray/capture only.

## Windows CI completed on 4 November 2026

- Code commit: `1dc163bb5730bc2af490430048e0adba2d67f49f`.
- [GitHub Actions run 41740441761](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41740441761): successful.
- Windows Release build: 0 warnings, 0 errors.
- Core tests on Windows: 10 passed, 0 failed, 0 skipped.
- Self-contained Windows x64 publish and artifact upload: successful.
- Artifact: `Sketchora-win-x64`, 71,279,661 bytes, artifact ID `13279444646`.
- This validates Windows compilation, automated core tests and packaging; it does not validate the interactive overlay, hardware input or meeting capture.

## Pending manual checks

- Native Windows UI execution and visual inspection.
- Touch/pen input, DPI and capture behavior.
- Receiver-side Zoom/Google Meet sharing and recordings.

See WINDOWS-CHECKLIST.md for the manual release gate. A successful cross-build does not validate desktop interaction.

## Version 2 implementation — v0.2.0-preview.1

- Lesson/page model, validated atomic save/load, recovery, Unicode text labels, temporary ink and configurable shortcuts are implemented in source.
- Local .NET 10 cross-build: 0 warnings/errors, including the Windows adapter test project.
- Local core tests: 42 passed, 0 failed, 0 skipped.
- The six Windows adapter tests ran in Windows CI; they do not simulate actual pen/touch hardware, IME typing or meeting capture.
- [Windows CI run 41744279619](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41744279619), code commit `b2d8e8fe518933e86f3d764542ca4913d2910b5b`: successful.
- Windows Release build: 0 warnings and 0 errors. Full Windows suite: 42 core + 6 WPF adapter tests passed, 0 failures, 0 skips.
- WPF tests cover ISF geometry/pressure/pen attributes, rejecting invalid ink before replacing work, canvas fitting/original positions, hidden-window/control construction on STA threads, and excluding temporary strokes from authored state.
- The first Windows run exposed an exact-equality assertion against ISF's converted ink units (width 4 returned as 3.9999874017370027). The test now allows 0.001 DIP width and 0.05 DIP coordinate tolerance while still checking pressure and attributes.
- Self-contained Windows x64 publish succeeded. Artifact `Sketchora-win-x64`: 71,310,007 bytes, ID `13282534262`.
- [Download the preview](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41744279619/artifacts/13282534262). This is a portable development build, not an installer or a completed hardware/meeting test.
- The interactive/hardware checks above remain pending. This is a development preview, not a completed live-teaching certification.


## Version 3 local verification — 4 November 2026

- Base GitHub commit: `38bb053d51713a6be2a078e306f0264dada82e68`.
- Final compiler: .NET SDK 10.0.403 on Linux, cross-targeting Windows.
- Full solution Release build: passed, **0 warnings / 0 errors**, including both test projects.
- Executed core tests: **53 passed / 0 failed / 0 skipped**. Result: `v3-core.trx`.
- Windows adapter suite: 14 tests compiled, **not executed** on this Linux host. Eight new adapter tests cover the added tools/input/capture behavior.
- Self-contained Windows x64 publish: succeeded. The portable package was cross-built; it has not been launched on Windows.
- XAML/project XML parsing and `git diff --check`: passed.
- The user explicitly approved pushing this update to `Nyi-Nyi-Zin/Sketchora`. Windows CI results are recorded below.
- Physical pen/multi-touch/proximity behavior, mixed-DPI and hot-plug alignment, Burmese IME, real region/clipboard interaction, Ghost Mode native recovery and receiver-side Zoom/Meet capture remain manual verification items. No claim of fully verified Epic Pen parity is made.


## Version 3 Windows CI — 4 November 2026

- Code commit: `16ab827ff5e45829a6c50454dedc161fd1ee2007`.
- [GitHub Actions run 41805713686](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41805713686): successful.
- Windows Release build: **0 warnings / 0 errors**.
- Full Windows suite: **53 core + 14 WPF adapter tests = 67 passed**, 0 failed, 0 skipped.
- Self-contained Windows x64 publish and artifact upload succeeded. Artifact `Sketchora-win-x64`: 71,334,265 bytes, ID `13309618067`.
- [Download the Windows preview](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41805713686/artifacts/13309618067).
- The first V3 Windows run exposed a canvas-initialization bug: committing the first finger stroke could cancel another pending stroke when no canvas identity had been rendered yet. The constructor now synchronizes the initial canvas before input; the existing concurrent-stroke/independent-undo regression test passes without weakening its assertions.
- This verifies automated WPF/software behavior and packaging. Physical hardware, mixed-DPI alignment, interactive Ghost/capture/IME workflows and receiver-side meeting checks remain pending in WINDOWS-CHECKLIST.md.


## Compact toolbar / raw pen update — v0.3.1-preview.1

- Verified code commit: `d9ee866972c846547280ee30389ed6a573743d72`.
- [Windows CI run 41811137768](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41811137768): successful.
- Release build: **0 warnings / 0 errors**. Full suite: **53 core + 20 WPF adapter cases = 73 passed**, 0 failures, 0 skips.
- Native window tests actually show the toolbar, open flyouts and pump the WPF dispatcher before checking bindings. They verify compact startup even with an old expanded preference, live expansion/collapse, unique actions, scrolling, four-DIP scrollbar width, remapped/unassigned shortcut tips and embedded icon loading.
- New geometry cases preserve raw corners for pen/highlighter/finger strokes and history, with curve fitting disabled. These do not replace a physical pen-lift check on the user's hardware.
- Compact and expanded WPF render outputs were retrieved from the test evidence and visually inspected; visible-UI pixel coverage is asserted by the render test.
- Self-contained Windows x64 publish succeeded. Artifact `Sketchora-win-x64`: **71,420,950 bytes**, ID `13312676497`.
- [Download the current preview](https://github.com/Nyi-Nyi-Zin/Sketchora/actions/runs/41811137768/artifacts/13312676497).
- Actual device input, mixed-DPI/hot-plug behavior, IME and receiver-side meeting capture retain their manual checklist requirements.

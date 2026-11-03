# Windows validation before live teaching

These are pending checks, not a report that the app passed them.

- [ ] Clean Windows 11 x64 account: restore, build, launch, quit and relaunch.
- [ ] Draw on browser, VS Code and a PDF; return to Interact and click/scroll/type normally.
- [ ] Draw on blank transparent desktop areas as well as existing ink; toolbar always remains clickable.
- [ ] Mouse, actual touchscreen and compatible active pen: no offset, duplicate strokes or stuck capture.
- [ ] Disable Touch drawing and verify finger contact cannot draw; pen still works.
- [ ] Test shapes with mouse, touch and pen. Undo/redo strokes and shapes, erasure and Clear.
- [ ] Undo, draw a new object and verify the old redo branch disappears.
- [ ] Switch desktop/whiteboard/blackboard and verify canvas isolation.
- [ ] Whiteboard in Interact mode stays visible until returning to desktop; no accidental deletion.
- [ ] Ctrl+Alt+D and Ctrl+Alt+H work from another app; a shortcut conflict leaves tray recovery available.
- [ ] Collapse, drag, hide, close and recover the toolbar. Test a short display with toolbar scrolling.
- [ ] Test 100%, 125%, 150% and 200% display scaling, display reconnect and sleep/wake.
- [ ] Halo tracks the cursor without intercepting input and hides outside the primary display.
- [ ] Export PNG: correct pixel size, annotation alignment, no toolbar/halo, no black background.
- [ ] Cancel export, choose an unwritable location and press emergency Hide during export; ink remains recoverable.
- [ ] Zoom full-display share: second participant sees ink, halo and whiteboard; confirm recording output.
- [ ] Google Meet full-display share: repeat receiver-side checks.
- [ ] Run a 60-minute lesson simulation; inspect latency and memory growth.
- [ ] Quit warns about ink in either canvas; No keeps the app and drawings available.
- [ ] Reopen restores preferences; malformed preferences still permit launch.

Do not claim Windows/pen/capture certification until these checks are completed on the supported configuration.

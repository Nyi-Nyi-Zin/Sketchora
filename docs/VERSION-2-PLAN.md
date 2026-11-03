# Sketchora — Version 2 Plan

Prepared: 4 November 2026.
Status: proposed scope, ready for review; no Version 2 implementation is claimed.
Release label: **v0.2.0**, the second development preview after v0.1.0.

## 1. Product outcome

Make Sketchora useful throughout an online lesson: briefly point at content, type a clear label, explain across multiple boards, then save and reopen the editable lesson later.

Example: open a Python lesson, draw on a code example, use a fading arrow to point at a variable, switch between two boards, add a Burmese explanation, and reopen those boards in the next class.

Windows 11 x64 remains the initial target. Keep C#, .NET 10 LTS, WPF, CommunityToolkit.Mvvm and the existing Win32 integration. Keep the app offline and local; no account or backend is required.

## 2. Verified baseline

Inspected repository baseline: `d890bfcddb9f6e7d187c3fd19f64bd915bf88a62`.

- Current release label in the app: v0.1.0.
- Implemented source: primary-display overlay, Draw/Interact modes, pen/highlighter/eraser, shapes, colors/width, undo/redo, desktop/board layers, pointer halo, PNG export, preferences and tray recovery.
- Windows CI has passed compilation, 10 core tests and portable packaging. See [VALIDATION.md](VALIDATION.md).
- Interactive Windows UI, touchscreen/pen, DPI and receiver-side Zoom/Meet capture checks remain pending. A successful build is not evidence those checks passed.
- Current ink is lost after quitting; PNG exports are flattened images. There is one board canvas and no text tool or fading ink.

## 3. Proposed release scope

| Priority | Feature | User outcome | Acceptance criteria |
| --- | --- | --- | --- |
| P0 | Windows interaction reliability | Teach without getting trapped in the overlay | Draw/Interact, tray recovery, focus, actual touch/pen input and receiver-side full-display sharing pass the baseline checklist on the supported setup |
| P1 | Editable lesson Save/Open | Reuse lesson notes in another class | Save and reopen both the desktop layer and every board page with ink, text, colors, coordinates and backgrounds preserved |
| P1 | Autosave and recovery | Recover recent work after an interrupted session | Maintain a separate local recovery copy; offer Restore/Discard at launch; preserve the last valid save if a write fails |
| P1 | Multiple whiteboard pages | Explain in steps without clearing earlier work | Add, rename, duplicate, delete, and navigate pages; each page retains its ink, text, background and independent undo/redo |
| P1 | Text tool | Add readable English and Burmese explanations | Create, edit and move Unicode text labels; support font size/color; Enter inserts a new line; explicit Done/Cancel controls commit/discard |
| P1 | Fading pen / laser drawing | Briefly point at content without repeated Clear | Provide 1-, 3- and 5-second lifetimes, timed from stroke completion; marks expire without altering permanent ink or reappearing after undo |
| P2 | Teaching controls | Use common controls quickly during lessons | Add a compact/expanded toolbar, visible selected tool/mode, a Fading tool button, a page indicator and shortcut settings with conflict feedback |

P0 and P1 define the second preview. P2 follows after those features are reliable; it must not delay fixes to input or data recovery.

## 4. Interaction decisions

### 4.1 Desktop and board pages

- Keep one desktop annotation layer, separate from the board page list.
- Desktop marks stay attached to screen coordinates. They will not track the document underneath when it scrolls.
- Each board page has its own white/black background. Changing a board background does not delete its contents.
- Navigate with visible Previous/Next buttons and a page count. Do not intercept ordinary browser/editor navigation keys globally.
- Deleting a board page is recoverable with an explicit restore action. Keep at least one board page.
- On opening a lesson created at another display size, explain the mismatch and offer Fit to display or Original size. Do not silently move annotations to guessed code/document positions.

### 4.2 Text entry and focus

- Click Text, then choose a canvas location to open a focused text editor.
- Commit text using Done, cancel using Cancel/Escape. Keep Enter available for multi-line text and IME behavior.
- Text entry must temporarily permit keyboard focus; the current no-activate overlay behavior cannot simply be reused unchanged.
- After Done/Cancel, return to the previous drawing tool and restore safe input routing. The emergency Hide action must still work while editing.
- Use a suitable installed Unicode font and test real Burmese typing, combining marks and mixed English/Burmese lines. Do not assume font rendering tests prove IME compatibility.
- Moving/editing text is in scope. General selection, rotation and resizing of every ink object is deferred.

### 4.3 Temporary and permanent ink

- Fading marks use a separate temporary layer; pen/highlighter strokes remain permanent.
- Use a monotonic clock for expiry. Start the lifetime on pointer release; do not erase the beginning of an unfinished stroke.
- Temporary marks are excluded from lesson saves, recovery files and permanent undo/redo history.
- Pause/end temporary drawing safely when changing mode or switching pages; do not carry expired marks onto another page.
- Provide a one-click Clear temporary marks action. Ordinary Clear targets permanent contents of the active canvas and remains undoable.
- PNG exports omit temporary marks by default; show that behavior in the export UI.

## 5. Local lesson format and recovery

File extension: `.sketchora`. It is an editable lesson file, distinct from a PNG screenshot.

Use a versioned document envelope with a schema version, active page, desktop-layer data, ordered board pages, per-page background, canvas dimensions and text objects. Preserve stylus pressure when storing ink.

Implementation preference: retain WPF InkCanvas for ink input. Serialize WPF ink through a dedicated adapter, using its supported ink serialization, and keep metadata/text in a versioned JSON envelope. The core document model should not depend on live Window or UIElement instances. Validate the format before adopting it; avoid writing a custom stroke renderer just to enable save/load.

- Store authored annotations and text. Do not silently embed screenshots of the desktop or underlying apps in lesson/recovery files.
- Save through a temporary file in the destination directory, then replace the target only after serialization succeeds.
- Load into a temporary document, validate it, and replace the active lesson only after success. Invalid/unsupported files must leave current work intact.
- Detect unsupported newer schema versions and show a clear message. Define file-size, page-count, text-length and embedded-ink limits before accepting input.
- Autosave after edits using a short debounce and at least every 30 seconds while dirty. Keep the recovery file separate from the user's named lesson.
- On restart, show recovery availability and modification time. Offer Restore or Discard; never silently overwrite the user's saved lesson.
- Track dirty state. New/Open/Quit offers Save/Discard/Cancel. A failed save leaves the document dirty and open.
- Export active page to PNG. PDF, all-pages PDF export and file-association registration are future work.

## 6. Implementation milestones

| Order | Milestone | Deliverable / gate |
| --- | --- | --- |
| 0 | Baseline validation | Run the existing Windows checklist, record observed issues, fix input/focus/capture failures before expanding interactive behavior |
| 1 | Lesson model and storage | Versioned document model, explicit Save/Open, dirty-state handling, corrupt-file protection and recovery tests |
| 2 | Board page workflow | Page collection, add/rename/duplicate/delete/recover, navigation and page-isolated history |
| 3 | Text editing | Focused Unicode editor, label rendering/edit/move and history integration |
| 4 | Fading ink | Separate ephemeral layer, configurable expiry and safe mode/page transitions |
| 5 | Teaching controls and preview | Compact UI, shortcut configuration, final regression checks and downloadable Windows preview |

Plan the storage/page model before the text and fading UI so those features have clear ownership. Do not begin several replacement drawing engines in parallel.

No calendar delivery date is promised by this plan. Reassess effort after the first Windows baseline session and after the lesson-format prototype.

## 7. Verification

### Automated checks

- Keep the existing 10 core tests and Windows build/publish workflow passing.
- Save/open round-trip: multiple pages, desktop data, Unicode text, backgrounds and ink payloads.
- Missing, truncated, oversized and unsupported-format input leaves the active lesson intact.
- Simulated save failure preserves the last valid lesson and dirty state; recovery remains independent.
- Page duplication does not alias mutable contents. Undo on one page does not change another page or the desktop layer.
- Dirty-state Save/Discard/Cancel follows the user's choice and never closes after a failed save.
- Fading expiry uses an injectable clock for boundary tests; temporary marks never enter permanent saves or history.
- Shortcut conflict handling retains the previous valid bindings and a recovery route.

### Windows/manual checks

- Run [WINDOWS-CHECKLIST.md](WINDOWS-CHECKLIST.md), including input routing, native capture, DPI, hardware input and receiver-side meeting capture.
- Type and edit actual Burmese/English multi-line text using the supported input methods. Verify Done/Cancel, focus restoration and emergency Hide.
- Teach a simulated lesson spanning at least three board pages, text labels and temporary marks; save, quit, reopen and continue editing.
- Interrupt the app after edits, restore the recovery document and confirm the named saved file was not overwritten.
- Run a 60-minute session with screen sharing; measure drawing responsiveness and memory. Investigate regressions against the measured v0.1.0 baseline rather than claiming a performance improvement without data.

## 8. Deferred scope

Simultaneous multi-monitor drawing, general object selection/rotation/resize, PDF import/export, built-in recording, real-time student collaboration, cloud sync, subscriptions, AI features and macOS/Linux ports are outside this preview.

Keep the existing portable distribution. Installer, signing and automatic updates need a separate release decision; the second preview does not require new paid services.

## 9. Release acceptance

Release v0.2.0 as a development preview only when the P0/P1 checks pass on the supported Windows setup, CI succeeds and known limitations are recorded. Describe the exact input devices and screen-sharing modes tested. Mark incomplete features as incomplete rather than treating compilation as a usability pass.

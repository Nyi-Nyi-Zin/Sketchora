# Teaching tools — v0.4.0-preview.1

The default remains the 60-DIP icon rail. Open the drawing-tool menu for ink tools and **Expand settings** for lesson, board and view controls. Button hover tips show the current shortcut; all actions can be assigned in Shortcut settings.

| Feature | Use | What is preserved |
| --- | --- | --- |
| Select, move, resize and copy | Choose **Select / resize** in the tool menu; click or lasso ink, drag the selection or its sizing handles. **Copy selected** offsets a duplicate; **Delete selected** removes it. | Ink and shapes, including undo/redo. Text labels retain their Text-tool editor; background images are not selectable objects. |
| Freeze screen | On desktop, expand **View & focus**, choose **Freeze screen**, then draw over the captured application. **Unfreeze screen**, **Use desktop** or emergency Hide releases it. | Authored ink stays in the lesson. Frozen desktop pixels are session-only and never saved into the lesson. |
| Multi-page PDF export | Expand the Lesson section and choose **Export PDF**. | Every board page in order, including backgrounds, worksheets, permanent ink and text. Desktop/frozen screen, temporary ink, spotlight, halo and UI are excluded. |
| Partial eraser | Choose **Partial eraser** in the tool menu. Its diameter follows the width control. | Cut only the touched part of a stroke, with undo/redo. Whole-stroke eraser remains available. |
| Image/PDF import | **Import image/PDF** accepts PNG, JPG, JPEG, BMP and PDF. For PDF choose an inclusive first/last page range. | Each imported page becomes an embedded worksheet board; annotation stays editable above it. Only a truly empty first board is replaced; otherwise new pages append. Original files need not remain available. |
| Zoom and pan | On boards or a frozen desktop, use zoom buttons or **Ctrl + mouse wheel**; choose **Pan** to drag. **Reset** returns to 100%. Range is 25–400%. | View-only changes; original ink/text/image coordinates stay intact. Changing page resets the view. Live desktop remains at 100%. |
| Grid / ruled background | On a board choose **Plain**, **Grid** or **Ruled**. White/black background remains independent. | Per-page, saved and undoable. **Remove worksheet image** is also undoable; it keeps annotations and patterns. |
| Spotlight | Expand **View & focus**, enable **Spotlight**, and adjust the radius. | Session-only enable state; radius is remembered. Spotlight is excluded from screenshots and PDF export. |

## File and rendering limits

- PDF export uses flattened, image-based A4 pages fitted in portrait/landscape to match the board aspect. Fonts, including Burmese glyphs, keep their rendered appearance. Exported text is not searchable/selectable; save `.sketchora` separately for editable content.
- PDF imports rasterize selected pages at up to 1920 pixels on the long edge. Imported PDF text and graphics become a worksheet image, not individually editable objects. Password-protected or unsupported PDFs report an error and keep the current lesson.
- Image source/PDF files are limited to 32 MiB. Embedded normalized PNG pages are limited to 12 MiB each and 32 megapixels; a saved lesson is limited to 32 MiB and 100 boards. Large ranges can exceed the combined lesson limit even when individual pages fit. Import is transactional: failure keeps existing pages and history.
- Schema 1 lessons remain readable. Worksheets/patterns use schema 2; older Sketchora versions cannot open those files. On display-size changes image placement transforms with ink/text, avoiding independent worksheet refitting.
- Fresh shortcut defaults add Ctrl+Alt+V (Select), Q (Partial eraser) and U (Pan). Existing complete custom maps retain their bindings, with newly introduced tools/actions initially Off; assign them explicitly or choose Defaults.
- Freeze and view transforms are temporary presentation tools. Physical touch, active-pen pressure/proximity, mixed-DPI alignment, secure/elevated apps and real meeting capture still require manual Windows testing. These additions do not establish full Epic Pen Pro parity.

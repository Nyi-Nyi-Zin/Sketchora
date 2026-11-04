using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Sketchora.App.Interop;
using Sketchora.App.ViewModels;
using Sketchora.App.Views;
using Sketchora.Core;
using Forms = System.Windows.Forms;

namespace Sketchora.App.Services;

internal sealed class AppController : IDisposable
{
    internal static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sketchora");
    private string RecoveryPath => Path.Combine(DataDirectory, "recovery.sketchora.json");
    private readonly PreferencesStore _store = new(Path.Combine(DataDirectory, "preferences.json"));
    private readonly LessonStore _lessons = new();
    private readonly LessonWorkspace _workspace = new();
    private AppPreferences _preferences;
    private readonly ToolbarViewModel _model;
    private readonly OverlayWindow _overlay;
    private readonly ToolbarWindow _toolbar;
    private readonly DispatcherTimer _autosave;
    private HotkeyService? _hotkeys;
    private Forms.NotifyIcon? _tray;
    private Icon? _appIcon;
    private TextEditorWindow? _textEditor;
    private RegionCaptureWindow? _capturePicker;
    private Task _recoveryTask = Task.CompletedTask;
    private long _lastRevision, _lastRecoveryRevision = -1;
    private long _lastEdit = Stopwatch.GetTimestamp(), _lastRecovery = Stopwatch.GetTimestamp();
    private bool? _resumeDrawing;
    private bool _ghost, _hidden, _quitting, _busy, _savingRecovery, _recoveryFailed, _disposed;

    public AppController()
    {
        _preferences = _store.Load();
        _model = new ToolbarViewModel(_preferences, Dispatch);
        _overlay = new OverlayWindow(_model, _workspace);
        _toolbar = new ToolbarWindow(_model);
        _toolbar.Closing += OnToolbarClosing;
        _overlay.DisplayChanged += () =>
        {
            PositionToolbar();
            var size = _overlay.CanvasSize;
            _workspace.ResizeCanvas(size.Width, size.Height);
        };
        _workspace.Changed += (_, _) =>
        {
            if (_lastRevision != _workspace.Revision)
            {
                _lastRevision = _workspace.Revision;
                _lastEdit = Stopwatch.GetTimestamp();
            }
        };
        _overlay.TextRequested += (position, text) => _ = RunAsync(() => EditTextAsync(position, text));
        _autosave = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => StartRecoverySave(), Application.Current.Dispatcher);
        _autosave.Stop();
    }

    public void Start()
    {
        _overlay.Show();
        _toolbar.Owner = _overlay;
        _toolbar.Show();
        PositionToolbar();
        _workspace.New(_overlay.CanvasSize.Width, _overlay.CanvasSize.Height);
        _hotkeys = new HotkeyService(_toolbar);
        var shortcuts = _preferences.EffectiveShortcuts();
        if (!ApplyBindings(shortcuts))
        {
            // A conflict in an optional tool must not remove emergency recovery.
            var recovery = ShortcutCatalog.Defaults();
            foreach (var name in recovery.Keys.ToArray()) if (name is not ("Draw" or "Hide" or "Ghost")) recovery[name] = 0;
            ApplyBindings(recovery);
            _model.Notice = "Some shortcuts are in use. Recovery shortcuts were attempted; use the tray to recover and open shortcut settings.";
        }
        CreateTray();
        _autosave.Start();
        _ = RunAsync(OfferRecoveryAsync);
    }

    private void PositionToolbar()
    {
        var work = SystemParameters.WorkArea;
        _toolbar.MaxHeight = Math.Max(200, work.Height - 16);
        _toolbar.Left = Math.Clamp(_preferences.ToolbarLeft ?? work.Right - _toolbar.Width - 20,
            work.Left + 8, Math.Max(work.Left + 8, work.Right - _toolbar.Width - 8));
        _toolbar.Top = Math.Clamp(_preferences.ToolbarTop ?? work.Top + 24,
            work.Top + 8, Math.Max(work.Top + 8, work.Bottom - Math.Min(_toolbar.ActualHeight, _toolbar.MaxHeight) - 8));
    }

    private bool ApplyBindings(IReadOnlyDictionary<string, uint> keys)
    {
        if (!ShortcutCatalog.IsValid(keys)) return false;
        var requested = keys.Where(pair => pair.Value != 0).ToDictionary(pair => pair.Value,
            pair => (Action)(() => InvokeShortcut(pair.Key)));
        if (_hotkeys?.Configure(requested) != true) return false;
        _model.UpdateShortcuts(keys);
        return true;
    }
    private void InvokeShortcut(string name)
    {
        if (name == "Hide") { if (_hidden) ShowAll(); else EmergencyHide(); return; }
        if (_busy) return;
        if (name == "Draw") { ToggleDrawing(); return; }
        if (name.StartsWith("Tool.", StringComparison.Ordinal) && Enum.TryParse<DrawingTool>(name[5..], out var tool))
        {
            if (_hidden) ShowAll();
            if (_model.SelectToolCommand.CanExecute(tool)) _model.SelectToolCommand.Execute(tool); return;
        }
        switch (name)
        {
            case "ToggleInk": _model.ShowInk = !_model.ShowInk; return;
            case "ToggleHalo": _model.HaloEnabled = !_model.HaloEnabled; return;
            case "ToggleTouch": _model.TouchEnabled = !_model.TouchEnabled; return;
            case "TogglePressure": _model.PressureEnabled = !_model.PressureEnabled; return;
            case "TogglePalm": _model.PalmRejection = !_model.PalmRejection; return;
            case "Collapse": _model.CollapseCommand.Execute(null); return;
            case "WidthUp": _model.Thickness++; return;
            case "WidthDown": _model.Thickness--; return;
            case "NextColor":
                var index = ToolbarViewModel.Palette.ToList().IndexOf(_model.Color);
                _model.Color = ToolbarViewModel.Palette[(index + 1) % ToolbarViewModel.Palette.Count]; return;
        }
        if (Enum.TryParse<CanvasAction>(name, out var action)) Dispatch(action);
    }
    private void ToggleGhost()
    {
        if (_hidden) ShowAll();
        _ghost = !_ghost;
        if (_ghost) _toolbar.Hide(); else { _toolbar.Show(); _toolbar.Activate(); }
        _model.Notice = _ghost ? "Ghost Mode: use its shortcut or the tray to restore the toolbar." : "Toolbar restored.";
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show toolbar", null, (_, _) => OnUi(ShowAll));
        menu.Items.Add("Ghost Mode", null, (_, _) => OnUi(() => { if (!_busy) ToggleGhost(); }));
        menu.Items.Add("Draw / interact", null, (_, _) => OnUi(ToggleDrawing));
        menu.Items.Add("Hide all / release screen", null, (_, _) => OnUi(EmergencyHide));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Sketchora", null, (_, _) => OnUi(() => _ = RunAsync(ExitAsync)));
        using (var stream = Application.GetResourceStream(new Uri("/Sketchora;component/Assets/Sketchora.ico", UriKind.Relative)).Stream)
            _appIcon = new Icon(stream);
        _tray = new Forms.NotifyIcon { Text = "Sketchora · screen annotation", Icon = _appIcon, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => OnUi(ShowAll);
    }
    private static void OnUi(Action action) => Application.Current.Dispatcher.Invoke(action);
    private void ToggleDrawing()
    {
        if (_busy) return;
        if (_hidden) ShowAll();
        _model.ToggleDrawCommand.Execute(null);
    }
    private void Dispatch(CanvasAction action)
    {
        if (action == CanvasAction.Hide) { EmergencyHide(); return; }
        if (_busy) return;
        if (action == CanvasAction.Ghost) { ToggleGhost(); return; }
        try
        {
            switch (action)
            {
                case CanvasAction.DuplicateSelection: _overlay.DuplicateSelection(); break;
                case CanvasAction.DeleteSelection: _overlay.DeleteSelection(); break;
                case CanvasAction.Freeze: _ = RunAsync(FreezeAsync); break;
                case CanvasAction.ExportPdf: _ = RunAsync(ExportPdfAsync); break;
                case CanvasAction.Import: _ = RunAsync(ImportAsync); break;
                case CanvasAction.ZoomIn: _overlay.ChangeZoom(1.25); break;
                case CanvasAction.ZoomOut: _overlay.ChangeZoom(.8); break;
                case CanvasAction.ResetView: _overlay.ResetView(); break;
                case CanvasAction.ToggleSpotlight: _model.SpotlightEnabled = !_model.SpotlightEnabled; break;
                case CanvasAction.PatternNone: _workspace.SetPattern(PagePattern.None); break;
                case CanvasAction.PatternGrid: _workspace.SetPattern(PagePattern.Grid); break;
                case CanvasAction.PatternRuled: _workspace.SetPattern(PagePattern.Ruled); break;
                case CanvasAction.RemoveImage: _workspace.RemovePageImage(); break;
                case CanvasAction.Undo: _workspace.Undo(); break;
                case CanvasAction.Redo: _workspace.Redo(); break;
                case CanvasAction.Clear: _workspace.Clear(); break;
                case CanvasAction.ClearTemporary: _overlay.ClearTemporary(); break;
                case CanvasAction.CycleBoard:
                    _workspace.ShowDesktop(!_workspace.IsDesktop);
                    _model.IsDrawing = !_workspace.IsDesktop; _model.ShowInk = true;
                    break;
                case CanvasAction.ToggleBackground:
                    _workspace.SetBackground(_workspace.ActivePage.Background == PageBackground.White ? PageBackground.Black : PageBackground.White);
                    break;
                case CanvasAction.PreviousPage: _workspace.Navigate(_workspace.IsDesktop ? 0 : -1); break;
                case CanvasAction.NextPage: _workspace.Navigate(_workspace.IsDesktop ? 0 : 1); break;
                case CanvasAction.AddPage: _workspace.AddPage(); break;
                case CanvasAction.DuplicatePage: _workspace.AddPage(duplicate: true); break;
                case CanvasAction.DeletePage: _workspace.DeletePage(); break;
                case CanvasAction.RestorePage: _workspace.RestorePage(); break;
                case CanvasAction.Export: _ = RunAsync(() => CaptureAsync(false, false)); break;
                case CanvasAction.CopyScreenshot: _ = RunAsync(() => CaptureAsync(true, false)); break;
                case CanvasAction.RegionScreenshot: _ = RunAsync(() => CaptureAsync(false, true)); break;
                case CanvasAction.CustomColor: _ = RunAsync(PickColorAsync); break;
                case CanvasAction.NewLesson: _ = RunAsync(NewAsync); break;
                case CanvasAction.OpenLesson: _ = RunAsync(OpenAsync); break;
                case CanvasAction.SaveLesson: _ = RunAsync(async () => { await SaveAsync(); }); break;
                case CanvasAction.SaveLessonAs: _ = RunAsync(async () => { await SaveAsync(saveAs: true); }); break;
                case CanvasAction.RenamePage: _ = RunAsync(RenameAsync); break;
                case CanvasAction.Settings: _ = RunAsync(SettingsAsync); break;
                case CanvasAction.Exit: _ = RunAsync(ExitAsync); break;
            }
        }
        catch (Exception ex) when (IsExpected(ex)) { ReportError(ex); }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _disposed) return;
        _busy = true; _resumeDrawing = null;
        var wasDrawing = _model.IsDrawing;
        _model.IsBusy = true;
        if (_ghost && !_hidden) _toolbar.Show();
        try { await _recoveryTask; await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsExpected(ex)) { ReportError(ex); }
        finally
        {
            _busy = false;
            if (!_quitting && !_disposed)
            {
                _model.IsBusy = false;
                if (!_hidden) _model.IsDrawing = _resumeDrawing ?? wasDrawing;
                if (_ghost) _toolbar.Hide();
            }
        }
    }
    private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException
        or ArgumentException or FormatException or NotSupportedException or InvalidOperationException or Win32Exception or System.Runtime.InteropServices.ExternalException;
    private void ReportError(Exception ex)
    {
        if (_quitting || _disposed) return;
        _model.Notice = "The operation could not finish. Your current lesson is still open.";
        if (!_hidden) MessageBox.Show(_toolbar, ex.Message, "Sketchora", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    public void EmergencyHide()
    {
        _hidden = true; _ghost = false;
        _overlay.SetFrozenFrame(null); _model.SpotlightEnabled = false;
        _model.IsDrawing = false;
        if (_textEditor is { IsVisible: true }) _textEditor.DialogResult = false;
        if (_capturePicker is { IsVisible: true }) _capturePicker.DialogResult = false;
        _overlay.CancelPendingShape();
        _overlay.Hide(); _toolbar.Hide();
    }
    private void ShowAll()
    {
        _overlay.Show(); _toolbar.Show(); _hidden = false; _ghost = false; _toolbar.Activate();
    }

    private async Task<bool> SaveAsync(bool saveAs = false)
    {
        var path = saveAs ? null : _workspace.FilePath;
        if (path is null)
        {
            var dialog = new SaveFileDialog { Title = "Save editable lesson", Filter = "Sketchora lesson (*.sketchora)|*.sketchora", DefaultExt = ".sketchora", AddExtension = true, FileName = "Lesson.sketchora" };
            if (dialog.ShowDialog(_toolbar) != true) return false;
            path = dialog.FileName;
        }
        var snapshot = _workspace.Snapshot; var revision = _workspace.Revision;
        await Task.Run(() => _lessons.Save(path, snapshot));
        _workspace.MarkSaved(path, revision);
        ClearRecovery();
        _model.Notice = "Editable lesson saved.";
        return true;
    }
    private Task<bool> ConfirmReplaceAsync() => UnsavedChangesGuard.CanReplaceAsync(_workspace.IsDirty, () =>
    {
        var answer = MessageBox.Show(_toolbar, "Save changes to the current lesson?\n\nYes = Save · No = Discard · Cancel = Keep editing",
            "Unsaved lesson", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return answer switch { MessageBoxResult.Yes => UnsavedChoice.Save, MessageBoxResult.No => UnsavedChoice.Discard, _ => UnsavedChoice.Cancel };
    }, () => SaveAsync());
    private async Task NewAsync()
    {
        if (!await ConfirmReplaceAsync()) return;
        _overlay.SetFrozenFrame(null);
        _workspace.New(_overlay.CanvasSize.Width, _overlay.CanvasSize.Height);
        ClearRecovery(); _lastRecoveryRevision = -1;
        _model.Notice = "New lesson. Board pages and desktop ink are separate.";
    }
    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog { Title = "Open editable lesson", Filter = "Sketchora lesson (*.sketchora)|*.sketchora" };
        if (dialog.ShowDialog(_toolbar) != true) return;
        var loaded = await Task.Run(() => _lessons.Load(dialog.FileName));
        // Decode every ink payload before changing the user's current lesson.
        InkSerializer.Validate(loaded);
        loaded = ChooseCanvasSize(loaded);
        if (!await ConfirmReplaceAsync()) return;
        _overlay.SetFrozenFrame(null);
        _workspace.Load(loaded, dialog.FileName);
        ClearRecovery(); _lastRecoveryRevision = -1;
        _model.Notice = "Lesson opened. Desktop ink remains fixed to screen coordinates.";
    }
    private LessonDocument ChooseCanvasSize(LessonDocument loaded, bool allowCancel = true)
    {
        var width = _overlay.CanvasSize.Width; var height = _overlay.CanvasSize.Height;
        if (Math.Abs(loaded.CanvasWidth - width) < 1 && Math.Abs(loaded.CanvasHeight - height) < 1) return loaded;
        var choice = MessageBox.Show(_toolbar,
            $"This lesson uses a {loaded.CanvasWidth:0} × {loaded.CanvasHeight:0} canvas. This display uses {width:0} × {height:0}.\n\n" +
            "Yes = Fit to this display\nNo = Keep original positions (some content may be outside the display)\nCancel = Do not open",
            "Different canvas size", allowCancel ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo, MessageBoxImage.Question, allowCancel ? MessageBoxResult.Cancel : MessageBoxResult.No);
        if (choice == MessageBoxResult.Cancel) throw new OperationCanceledException();
        return InkSerializer.Resize(loaded, width, height, choice == MessageBoxResult.Yes);
    }
    private async Task OfferRecoveryAsync()
    {
        if (!File.Exists(RecoveryPath)) return;
        var recovered = await Task.Run(() => _lessons.LoadRecovery(RecoveryPath));
        InkSerializer.Validate(recovered.Lesson);
        var answer = MessageBox.Show(_toolbar,
            $"Restore the local lesson recovery from {recovered.SavedAt.LocalDateTime:g}?\n\nYes = Restore · No = Discard recovery",
            "Recover lesson", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) { ClearRecovery(); return; }
        var loaded = ChooseCanvasSize(recovered.Lesson, allowCancel: false);
        // Recovery always saves as a new chosen file, preserving the named lesson.
        _workspace.Load(loaded, recovered: true);
        _lastRecoveryRevision = _workspace.Revision;
        _model.Notice = "Recovery restored. Use Save lesson to choose a file.";
    }
    private Task RenameAsync()
    {
        var name = LessonDialogs.Rename(_toolbar, _workspace.ActivePage.Name);
        if (name is not null) _workspace.RenamePage(name);
        return Task.CompletedTask;
    }
    private Task EditTextAsync(System.Windows.Point position, TextLabel? original)
    {
        var pageId = _workspace.ActivePage.Id;
        var label = original ?? new TextLabel { X = position.X, Y = position.Y, Color = _model.Color, FontSize = _model.TextSize };
        _textEditor = new TextEditorWindow(_toolbar, label, original is not null);
        try
        {
            if (_textEditor.ShowDialog() == true && !_hidden && _workspace.ActivePage.Id == pageId)
            {
                if (_textEditor.DeleteRequested) _workspace.DeleteText(label.Id);
                else _workspace.UpsertText(label with { Text = _textEditor.LabelText, FontSize = _textEditor.LabelSize, Color = _textEditor.LabelColor });
                _model.TextSize = _textEditor.LabelSize;
            }
        }
        finally { _textEditor = null; _model.ReturnFromText(); }
        return Task.CompletedTask;
    }
    private Task SettingsAsync()
    {
        LessonDialogs.Shortcuts(_toolbar, _preferences.EffectiveShortcuts(), keys =>
        {
            if (!ShortcutCatalog.IsValid(keys)) return "Choose unique keys; Draw, Hide and Ghost must stay enabled.";
            if (!ApplyBindings(keys)) return "A shortcut is already in use. Previous bindings are unchanged.";
            _preferences = _preferences with { Shortcuts = new(keys) };
            var saved = SavePreferences();
            _model.Notice = saved ? "Shortcut settings applied." : "Shortcuts work for this session, but preferences could not be saved.";
            return null;
        });
        return Task.CompletedTask;
    }
    private Task PickColorAsync()
    {
        if (ColorPicker.Choose(_toolbar, _model.Color) is { } color) _model.Color = color;
        return Task.CompletedTask;
    }
    private async Task FreezeAsync()
    {
        if (!_workspace.IsDesktop && !_model.FrozenScreen)
        { _model.Notice = "Boards already support zoom/pan. Switch to desktop to freeze an application."; return; }
        if (_model.FrozenScreen)
        {
            _overlay.SetFrozenFrame(null); _model.Notice = "Screen unfrozen. Authored ink is unchanged."; return;
        }
        _overlay.Hide(); _toolbar.Hide();
        try
        {
            await Task.Delay(160);
            if (_hidden) return;
            NativeMethods.DwmFlush();
            using var desktop = ScreenCapture.CaptureDesktop();
            var frame = ScreenCapture.ToBitmapSource(desktop);
            _workspace.ShowDesktop(true); _overlay.SetFrozenFrame(frame);
            _model.ShowInk = true; _resumeDrawing = true;
            _model.Notice = "Screen frozen. Draw, zoom or pan; Use desktop or Unfreeze releases it. Frozen pixels are session-only.";
        }
        finally { if (!_hidden) { _overlay.Show(); if (!_ghost) _toolbar.Show(); } }
    }
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Title = "Import worksheet as board pages", Filter = "Image or PDF|*.png;*.jpg;*.jpeg;*.bmp;*.pdf|Images|*.png;*.jpg;*.jpeg;*.bmp|PDF|*.pdf" };
        if (dialog.ShowDialog(_toolbar) != true) return;
        IReadOnlyList<LessonPage> pages;
        if (string.Equals(Path.GetExtension(dialog.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            pages = await PdfImport.ReadAsync(dialog.FileName, total => LessonDialogs.PdfRange(_toolbar, total, _workspace.ImportCapacity), () => _hidden);
        else pages = new[] { LessonRenderer.ImportImage(dialog.FileName) };
        if (_hidden) return;
        _workspace.ImportPages(pages); _resumeDrawing = true;
        _model.Notice = $"Imported {pages.Count} board page(s). Save lesson to preserve worksheets and annotations.";
    }
    private async Task ExportPdfAsync()
    {
        var dialog = new SaveFileDialog { Title = "Export all board pages to PDF", Filter = "PDF document (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true, FileName = "Sketchora-lesson.pdf" };
        if (dialog.ShowDialog(_toolbar) != true) return;
        var snapshot = _workspace.Snapshot; var pages = new List<PdfRasterPage>();
        for (var i = 0; i < snapshot.Pages.Count; i++)
        {
            if (_hidden) throw new OperationCanceledException();
            _model.Notice = $"Exporting board {i + 1} / {snapshot.Pages.Count}…";
            pages.Add(LessonRenderer.RenderPage(snapshot.Pages[i], snapshot.CanvasWidth, snapshot.CanvasHeight));
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        if (_hidden) throw new OperationCanceledException();
        await Task.Run(() => RasterPdf.Save(dialog.FileName, pages));
        _model.Notice = $"Exported {pages.Count} board page(s). PDF pages are flattened images; the editable lesson stays separate.";
    }
    private async Task CaptureAsync(bool clipboard, bool region)
    {
        try
        {
            _overlay.SuspendHalo = true; _toolbar.Hide();
            await Task.Delay(180);
            if (_hidden) return;
            NativeMethods.DwmFlush();
            using var desktop = ScreenCapture.CaptureDesktop();
            System.Drawing.Bitmap? cropped = null;
            try
            {
                if (region)
                {
                    var picker = new RegionCaptureWindow(desktop);
                    _capturePicker = picker;
                    bool? selected;
                    try { selected = picker.ShowDialog(); }
                    finally { _capturePicker = null; }
                    if (selected != true || picker.Selection is not { } bounds) return;
                    cropped = desktop.Clone(new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height), PixelFormat.Format32bppRgb);
                    clipboard = picker.CopyRequested;
                }
                var bitmap = cropped ?? desktop;
                if (_hidden) return;
                if (clipboard)
                {
                    System.Windows.Clipboard.SetImage(ScreenCapture.ToBitmapSource(bitmap));
                    _model.Notice = "Screenshot copied to clipboard.";
                }
                else
                {
                    // Restore an owner before opening the save dialog, including in Ghost Mode.
                    _toolbar.Show();
                    var dialog = new SaveFileDialog { Title = "Save screenshot", Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", AddExtension = true, FileName = $"Sketchora-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
                    if (dialog.ShowDialog(_toolbar) != true) return;
                    bitmap.Save(dialog.FileName, ImageFormat.Png);
                    _model.Notice = "PNG saved. Temporary marks, halo and toolbar were excluded.";
                }
            }
            finally { cropped?.Dispose(); }
        }
        finally
        {
            _overlay.SuspendHalo = false;
            if (!_hidden && !_quitting && !_ghost) _toolbar.Show();
        }
    }

    private void StartRecoverySave()
    {
        if (_busy || _savingRecovery || _disposed || !_workspace.IsDirty || _workspace.Revision == _lastRecoveryRevision) return;
        if (_recoveryFailed && Stopwatch.GetElapsedTime(_lastRecovery).TotalSeconds < 30) return;
        if (Stopwatch.GetElapsedTime(_lastEdit).TotalSeconds < 2 && Stopwatch.GetElapsedTime(_lastRecovery).TotalSeconds < 30) return;
        var snapshot = _workspace.Snapshot; var revision = _workspace.Revision;
        var recovery = new RecoveryDocument { Lesson = snapshot, OriginalPath = _workspace.FilePath };
        _savingRecovery = true;
        _recoveryTask = SaveRecoveryAsync(recovery, revision);
    }
    private async Task SaveRecoveryAsync(RecoveryDocument recovery, long revision)
    {
        try
        {
            await Task.Run(() => _lessons.SaveRecovery(RecoveryPath, recovery));
            _lastRecoveryRevision = revision;
            _recoveryFailed = false;
            _lastRecovery = Stopwatch.GetTimestamp();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _recoveryFailed = true; _lastRecovery = Stopwatch.GetTimestamp();
            _model.Notice = "Autosave failed. Use Save lesson to keep your work. " + ex.Message;
        }
        finally { _savingRecovery = false; }
    }
    private void ClearRecovery()
    {
        try { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); }
        catch (Exception ex) when (IsExpected(ex)) { _model.Notice = "Could not clear the older recovery copy. " + ex.Message; }
    }
    private void OnToolbarClosing(object? sender, CancelEventArgs e)
    {
        if (_quitting) return;
        e.Cancel = true; EmergencyHide();
    }
    private async Task ExitAsync()
    {
        ShowAll();
        if (!await ConfirmReplaceAsync()) return;
        SavePreferences(); ClearRecovery();
        _quitting = true; _autosave.Stop();
        Application.Current.Shutdown();
    }
    private bool SavePreferences()
    {
        _preferences = _preferences with
        {
            Color = _model.Color, Thickness = _model.Thickness, PointerHalo = _model.HaloEnabled, TouchDrawing = _model.TouchEnabled,
            ToolbarLeft = _toolbar.Left, ToolbarTop = _toolbar.Top, ExpandedToolbar = false,
            FadeSeconds = _model.FadeSeconds, TextSize = _model.TextSize,
            ToolWidths = _model.ToolWidths, PressureEnabled = _model.PressureEnabled, PalmRejection = _model.PalmRejection, SpotlightRadius = _model.SpotlightRadius
        };
        try { _store.Save(_preferences); return true; }
        catch (Exception ex) when (IsExpected(ex)) { _model.Notice = "Could not save preferences. " + ex.Message; return false; }
    }
    public void Dispose()
    {
        _disposed = true; _autosave.Stop(); _hotkeys?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _appIcon?.Dispose();
    }
}

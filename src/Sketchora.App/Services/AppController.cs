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
    private TextEditorWindow? _textEditor;
    private Task _recoveryTask = Task.CompletedTask;
    private long _lastRevision, _lastRecoveryRevision = -1;
    private long _lastEdit = Stopwatch.GetTimestamp(), _lastRecovery = Stopwatch.GetTimestamp();
    private bool _hidden, _quitting, _busy, _savingRecovery, _recoveryFailed, _disposed;

    public AppController()
    {
        _preferences = _store.Load();
        _model = new ToolbarViewModel(_preferences, Dispatch);
        _overlay = new OverlayWindow(_model, _workspace);
        _toolbar = new ToolbarWindow(_model);
        _toolbar.Closing += OnToolbarClosing;
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
        if (!ApplyBindings(_preferences.DrawShortcut, _preferences.HideShortcut))
        {
            if (ApplyBindings('D', 'H'))
            {
                _preferences = _preferences with { DrawShortcut = 'D', HideShortcut = 'H' };
                _model.UpdateShortcuts('D', 'H');
            }
            _model.Notice = "A shortcut is in use. Open shortcut settings or use the tray recovery menu.";
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

    private bool ApplyBindings(char draw, char hide) => _hotkeys?.Configure(draw, hide, ToggleDrawing,
        () => { if (_hidden) ShowAll(); else EmergencyHide(); }) == true;
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show toolbar", null, (_, _) => OnUi(ShowAll));
        menu.Items.Add("Draw / interact", null, (_, _) => OnUi(ToggleDrawing));
        menu.Items.Add("Hide all / release screen", null, (_, _) => OnUi(EmergencyHide));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Sketchora", null, (_, _) => OnUi(() => _ = RunAsync(ExitAsync)));
        _tray = new Forms.NotifyIcon { Text = "Sketchora · screen annotation", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => OnUi(ShowAll);
    }
    private static void OnUi(Action action) => Application.Current.Dispatcher.Invoke(action);
    private void ToggleDrawing()
    {
        if (_busy) return;
        ShowAll(); _model.ToggleDrawCommand.Execute(null);
    }
    private void Dispatch(CanvasAction action)
    {
        if (action == CanvasAction.Hide) { EmergencyHide(); return; }
        if (_busy) return;
        try
        {
            switch (action)
            {
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
                case CanvasAction.Export: _ = RunAsync(ExportAsync); break;
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
        _busy = true;
        var wasDrawing = _model.IsDrawing;
        _model.IsBusy = true;
        try { await _recoveryTask; await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsExpected(ex)) { ReportError(ex); }
        finally
        {
            _busy = false;
            if (!_quitting && !_disposed)
            {
                _model.IsBusy = false;
                if (!_hidden) _model.IsDrawing = wasDrawing;
            }
        }
    }
    private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException
        or ArgumentException or InvalidOperationException or Win32Exception or System.Runtime.InteropServices.ExternalException;
    private void ReportError(Exception ex)
    {
        if (_quitting || _disposed) return;
        _model.Notice = "The operation could not finish. Your current lesson is still open.";
        if (!_hidden) MessageBox.Show(_toolbar, ex.Message, "Sketchora", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    public void EmergencyHide()
    {
        _hidden = true;
        _model.IsDrawing = false;
        if (_textEditor is { IsVisible: true }) _textEditor.DialogResult = false;
        _overlay.CancelPendingShape();
        _overlay.Hide(); _toolbar.Hide();
    }
    private void ShowAll()
    {
        _overlay.Show(); _toolbar.Show(); _hidden = false; _toolbar.Activate();
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
        LessonDialogs.Shortcuts(_toolbar, _preferences.DrawShortcut, _preferences.HideShortcut, (draw, hide) =>
        {
            if (!AppPreferences.ValidShortcuts(draw, hide)) return "Choose two different letters.";
            if (!ApplyBindings(draw, hide)) return "A shortcut is already in use. Your previous bindings are unchanged.";
            _preferences = _preferences with { DrawShortcut = draw, HideShortcut = hide };
            _model.UpdateShortcuts(draw, hide);
            var saved = SavePreferences();
            _model.Notice = saved ? "Shortcut settings applied." : "Shortcuts work for this session, but preferences could not be saved.";
            return null;
        });
        return Task.CompletedTask;
    }
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog { Title = "Save PNG (temporary marks excluded)", Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", AddExtension = true, FileName = $"Sketchora-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        if (dialog.ShowDialog(_toolbar) != true) return;
        try
        {
            _overlay.SuspendHalo = true; _toolbar.Hide();
            await Task.Delay(180);
            if (_hidden) return;
            NativeMethods.DwmFlush();
            using var bitmap = ScreenCapture.CapturePrimaryDisplay();
            bitmap.Save(dialog.FileName, ImageFormat.Png);
            _model.Notice = "PNG saved. Temporary marks, halo and toolbar were excluded.";
        }
        finally { _overlay.SuspendHalo = false; if (!_hidden && !_quitting) _toolbar.Show(); }
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
            ToolbarLeft = _toolbar.Left, ToolbarTop = _toolbar.Top, ExpandedToolbar = _model.IsExpanded,
            FadeSeconds = _model.FadeSeconds, TextSize = _model.TextSize
        };
        try { _store.Save(_preferences); return true; }
        catch (Exception ex) when (IsExpected(ex)) { _model.Notice = "Could not save preferences. " + ex.Message; return false; }
    }
    public void Dispose()
    {
        _disposed = true; _autosave.Stop(); _hotkeys?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
    }
}

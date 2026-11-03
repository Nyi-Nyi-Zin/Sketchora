using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using Microsoft.Win32;
using Sketchora.App.Interop;
using Sketchora.App.ViewModels;
using Sketchora.App.Views;
using Sketchora.Core;
using Forms = System.Windows.Forms;

namespace Sketchora.App.Services;

internal sealed class AppController : IDisposable
{
    internal static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sketchora");
    private readonly PreferencesStore _store = new(Path.Combine(DataDirectory, "preferences.json"));
    private readonly AppPreferences _preferences;
    private readonly ToolbarViewModel _model;
    private readonly OverlayWindow _overlay;
    private readonly ToolbarWindow _toolbar;
    private HotkeyService? _hotkeys;
    private Forms.NotifyIcon? _tray;
    private bool _hidden;
    private bool _quitting;
    private bool _exporting;

    public AppController()
    {
        _preferences = _store.Load();
        _model = new ToolbarViewModel(_preferences, Dispatch);
        _overlay = new OverlayWindow(_model);
        _toolbar = new ToolbarWindow(_model);
        _toolbar.Closing += OnToolbarClosing;
    }

    public void Start()
    {
        _overlay.Show();
        _toolbar.Owner = _overlay;
        _toolbar.Show();
        PositionToolbar();
        _hotkeys = new HotkeyService(_toolbar);
        var drawKey = _hotkeys.Register(0x44, ToggleDrawing);
        var hideKey = _hotkeys.Register(0x48, () => { if (_hidden) ShowAll(); else EmergencyHide(); });
        if (!drawKey || !hideKey)
            _model.Notice = "A shortcut is in use by another app. Use the toolbar or tray menu.";
        CreateTray();
    }

    private void PositionToolbar()
    {
        var work = SystemParameters.WorkArea;
        // Keep the full panel reachable on shorter displays.
        _toolbar.MaxHeight = work.Height - 16;
        _toolbar.Left = Math.Clamp(_preferences.ToolbarLeft ?? work.Right - _toolbar.Width - 20,
            work.Left + 8, Math.Max(work.Left + 8, work.Right - _toolbar.Width - 8));
        _toolbar.Top = Math.Clamp(_preferences.ToolbarTop ?? work.Top + 24,
            work.Top + 8, Math.Max(work.Top + 8, work.Bottom - _toolbar.ActualHeight - 8));
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show toolbar", null, (_, _) => OnUi(ShowAll));
        menu.Items.Add("Draw / interact", null, (_, _) => OnUi(ToggleDrawing));
        menu.Items.Add("Hide all / release screen", null, (_, _) => OnUi(EmergencyHide));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Sketchora", null, (_, _) => OnUi(RequestExit));
        _tray = new Forms.NotifyIcon
        {
            Text = "Sketchora · screen annotation",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => OnUi(ShowAll);
    }

    private static void OnUi(Action action) => Application.Current.Dispatcher.Invoke(action);

    private void ToggleDrawing()
    {
        if (_exporting) return;
        ShowAll();
        _model.ToggleDrawCommand.Execute(null);
    }

    private void Dispatch(CanvasAction action)
    {
        switch (action)
        {
            case CanvasAction.Undo: _overlay.Undo(); break;
            case CanvasAction.Redo: _overlay.Redo(); break;
            case CanvasAction.Clear: _overlay.ClearInk(); break;
            case CanvasAction.Export: _ = ExportAsync(); break;
            case CanvasAction.Hide: EmergencyHide(); break;
            case CanvasAction.Exit: RequestExit(); break;
        }
    }

    public void EmergencyHide()
    {
        _model.IsDrawing = false;
        _overlay.CancelPendingShape();
        _overlay.Hide();
        _toolbar.Hide();
        _hidden = true;
    }

    private void ShowAll()
    {
        _overlay.Show();
        _toolbar.Show();
        _hidden = false;
        _toolbar.Activate();
    }

    private async Task ExportAsync()
    {
        if (_exporting) return;
        _exporting = true;
        var wasDrawing = _model.IsDrawing;
        try
        {
            _model.IsDrawing = false;
            var dialog = new SaveFileDialog
            {
                Title = "Save Sketchora screenshot",
                Filter = "PNG image (*.png)|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = $"Sketchora-{DateTime.Now:yyyyMMdd-HHmmss}.png"
            };
            if (dialog.ShowDialog(_toolbar) != true) return;
            _overlay.SuspendHalo = true;
            _toolbar.Hide();
            await Task.Delay(180);
            if (_hidden) return;
            NativeMethods.DwmFlush();
            var bounds = NativeMethods.PrimaryDisplayBounds();
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0,
                    new System.Drawing.Size(bounds.Width, bounds.Height),
                    CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
            bitmap.Save(dialog.FileName, ImageFormat.Png);
            _model.Notice = "PNG saved. Only the currently visible canvas was captured.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Runtime.InteropServices.ExternalException or ArgumentException or Win32Exception)
        {
            _model.Notice = "Could not save PNG. Your ink is still here.";
            _toolbar.Show();
            MessageBox.Show(_toolbar, "Could not save the screenshot. " + ex.Message, "Sketchora");
        }
        finally
        {
            _overlay.SuspendHalo = false;
            if (!_quitting && !_hidden)
            {
                _toolbar.Show();
                _model.IsDrawing = wasDrawing;
            }
            _exporting = false;
        }
    }

    private void OnToolbarClosing(object? sender, CancelEventArgs e)
    {
        if (_quitting) return;
        e.Cancel = true;
        EmergencyHide();
    }

    private void RequestExit()
    {
        if (_exporting) { _model.Notice = "Finish saving the PNG before quitting."; return; }
        if (_overlay.HasInk)
        {
            ShowAll();
            _model.IsDrawing = false;
            if (MessageBox.Show(_toolbar,
                "Close Sketchora and discard the ink on both canvases?\n\n" +
                "Choose No to save a PNG first. Editable session saving is not available yet.",
                "Close Sketchora?", MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        }
        _quitting = true;
        SavePreferences();
        Application.Current.Shutdown();
    }

    private void SavePreferences()
    {
        try
        {
            _store.Save(new AppPreferences
            {
                Color = _model.Color,
                Thickness = _model.Thickness,
                PointerHalo = _model.HaloEnabled,
                TouchDrawing = _model.TouchEnabled,
                ToolbarLeft = _toolbar.Left,
                ToolbarTop = _toolbar.Top
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Sketchora could not save preferences. " + ex.Message, "Sketchora");
        }
    }

    public void Dispose()
    {
        _hotkeys?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }
    }
}

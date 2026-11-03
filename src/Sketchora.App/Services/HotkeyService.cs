using System.Windows;
using System.Windows.Interop;
using Sketchora.App.Interop;

namespace Sketchora.App.Services;

internal sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public HotkeyService(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("No toolbar window handle.");
        _source.AddHook(WndProc);
    }

    public bool Register(uint key, Action action)
    {
        var id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id,
                NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat, key))
            return false;
        _actions.Add(id, action);
        return true;
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_source.IsDisposed) return;
        foreach (var id in _actions.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _source.RemoveHook(WndProc);
        _actions.Clear();
    }
}

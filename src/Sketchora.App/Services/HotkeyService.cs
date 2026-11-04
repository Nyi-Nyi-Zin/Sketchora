using System.Windows;
using System.Windows.Interop;
using Sketchora.App.Interop;
using Sketchora.Core;

namespace Sketchora.App.Services;

internal sealed class HotkeyService : IDisposable, IShortcutRegistrar
{
    private readonly HwndSource _source;
    private readonly ShortcutBindings _bindings;
    public HotkeyService(Window window)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle())
            ?? throw new InvalidOperationException("No toolbar handle.");
        _bindings = new ShortcutBindings(this);
        _source.AddHook(WndProc);
    }
    public bool Configure(IReadOnlyDictionary<uint, Action> requested) => _bindings.TryConfigure(requested);
    public bool Register(int id, uint key) => NativeMethods.RegisterHotKey(_source.Handle, id,
        NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat, key);
    public void Unregister(int id) { if (!_source.IsDisposed) NativeMethods.UnregisterHotKey(_source.Handle, id); }
    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey) handled = _bindings.Invoke(wParam.ToInt32());
        return 0;
    }
    public void Dispose()
    {
        _bindings.Dispose();
        if (!_source.IsDisposed) _source.RemoveHook(WndProc);
    }
}

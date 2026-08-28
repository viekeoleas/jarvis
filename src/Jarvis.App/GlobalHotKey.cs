using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Jarvis.App;

internal sealed partial class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 0x4A41;
    private const int WmHotKey = 0x0312;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly Window _window;
    private readonly Action _onPressed;
    private HwndSource? _source;
    private nint _handle;
    private bool _registered;

    public GlobalHotKey(Window window, Action onPressed)
    {
        _window = window;
        _onPressed = onPressed;
        _handle = new WindowInteropHelper(_window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle)
            ?? throw new InvalidOperationException("Jarvis could not create a window handle.");
        _source.AddHook(WindowProcedure);

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(Key.O);
        _registered = RegisterHotKey(_handle, HotKeyId, ModShift | ModWin, virtualKey);
        if (!_registered)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Win+Shift+O could not be registered.");
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_handle, HotKeyId);
            _registered = false;
        }

        _source?.RemoveHook(WindowProcedure);
        _source = null;
    }

    private nint WindowProcedure(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmHotKey && wParam == HotKeyId)
        {
            _onPressed();
            handled = true;
        }

        return 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}

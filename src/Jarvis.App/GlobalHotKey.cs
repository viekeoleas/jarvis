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
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWin = 0x5B;
    private const int VkRightWin = 0x5C;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly Window _window;
    private readonly Action _onPressed;
    private HwndSource? _source;
    private nint _handle;
    private nint _keyboardHook;
    private bool _registered;
    private bool _hotKeyHeld;
    private LowLevelKeyboardProc? _keyboardProc;

    public GlobalHotKey(Window window, Action onPressed)
    {
        _window = window;
        _onPressed = onPressed;
        _handle = new WindowInteropHelper(_window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle)
            ?? throw new InvalidOperationException("Jarvis could not create a window handle.");
        _source.AddHook(WindowProcedure);

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(Key.D2);
        _registered = RegisterHotKey(_handle, HotKeyId, ModShift | ModWin, virtualKey);
        if (!_registered)
        {
            _keyboardProc = KeyboardProcedure;
            _keyboardHook = SetWindowsHookEx(
                WhKeyboardLl,
                _keyboardProc,
                GetModuleHandle(null),
                0);
            if (_keyboardHook == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Win+Shift+2 could not be registered or intercepted.");
            }
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_handle, HotKeyId);
            _registered = false;
        }

        if (_keyboardHook != 0)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }

        _keyboardProc = null;

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

    private nint KeyboardProcedure(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var message = unchecked((int)wParam);
            var virtualKey = Marshal.ReadInt32(lParam);
            var isTargetKey = virtualKey == KeyInterop.VirtualKeyFromKey(Key.D2);
            if (isTargetKey && message is WmKeyDown or WmSysKeyDown &&
                IsKeyDown(VkShift) &&
                (IsKeyDown(VkLeftWin) || IsKeyDown(VkRightWin)) &&
                !IsKeyDown(VkControl) &&
                !IsKeyDown(VkMenu))
            {
                if (!_hotKeyHeld)
                {
                    _hotKeyHeld = true;
                    _window.Dispatcher.BeginInvoke(_onPressed);
                }

                return 1;
            }

            if (isTargetKey && message is WmKeyUp or WmSysKeyUp && _hotKeyHeld)
            {
                _hotKeyHeld = false;
                return 1;
            }
        }

        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private static bool IsKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        nint module,
        uint threadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(
        nint hook,
        int code,
        nint wParam,
        nint lParam);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);
}

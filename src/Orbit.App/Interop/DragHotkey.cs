using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Orbit.App.Interop;

public sealed class DragHotkey : IDisposable
{
    private readonly Func<MenuHotkey> _hotkey;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Native.HookProc _keyboardProc, _mouseProc;
    private IntPtr _keyboard, _mouse;
    private uint _threadId;
    private bool _mouseDown, _dragged, _spaceConsumed;
    private Native.Point _origin;
    private Exception? _failure;
    public event Action<Native.Point>? Requested;
    public event Action? Released;
    public event Action? Cancelled;

    public DragHotkey(Func<MenuHotkey> hotkey)
    {
        _hotkey = hotkey;
        _keyboardProc = Keyboard; _mouseProc = Mouse;
        _thread = new Thread(Run) { IsBackground = true, Name = "Orbit input hooks" };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Không khởi tạo được phím gọi menu.");
        if (_failure != null) throw _failure;
    }
    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        Native.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        try
        {
            var module = Native.GetModuleHandle(null);
            _keyboard = Native.SetWindowsHookEx(13, _keyboardProc, module, 0);
            _mouse = Native.SetWindowsHookEx(14, _mouseProc, module, 0);
            if (_keyboard == IntPtr.Zero || _mouse == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _ready.Set();
            while (Native.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
        }
        catch (Exception ex) { _failure = ex; _ready.Set(); }
        finally
        {
            if (_keyboard != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyboard);
            if (_mouse != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouse);
        }
    }
    private IntPtr Mouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var id = wParam.ToInt32();
            if (id == 0x0201) { _origin = Marshal.PtrToStructure<Native.MouseData>(lParam).Point; _mouseDown = true; _dragged = false; }
            else if (id == 0x0200 && _mouseDown && !_dragged)
            {
                var point = Marshal.PtrToStructure<Native.MouseData>(lParam).Point;
                _dragged = Math.Abs(point.X - _origin.X) >= 6 || Math.Abs(point.Y - _origin.Y) >= 6;
            }
            else if (id == 0x0202) { _mouseDown = false; _dragged = false; Released?.Invoke(); }
        }
        return Native.CallNextHookEx(_mouse, code, wParam, lParam);
    }
    private IntPtr Keyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<Native.KeyboardData>(lParam);
            var down = wParam.ToInt32() is 0x0100 or 0x0104;
            if (data.Key == 0x1B && down) Cancelled?.Invoke(); // Pass Esc on to the native drag source.
            if (data.Key == 0x20)
            {
                if (!down && _spaceConsumed) { _spaceConsumed = false; return new IntPtr(1); }
                if (down && _spaceConsumed) return new IntPtr(1);
                var control = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
                var shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
                var alt = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
                var windows = (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0;
                if (down && MenuHotkeys.ShouldActivate(_hotkey(), _mouseDown && (Native.GetAsyncKeyState(1) & 0x8000) != 0, _dragged, control, shift, alt, windows))
                {
                    _spaceConsumed = true; Native.GetCursorPos(out var point); Requested?.Invoke(point); return new IntPtr(1);
                }
            }
        }
        return Native.CallNextHookEx(_keyboard, code, wParam, lParam);
    }
    public void Dispose()
    {
        Native.PostThreadMessage(_threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000); _ready.Dispose();
    }
}

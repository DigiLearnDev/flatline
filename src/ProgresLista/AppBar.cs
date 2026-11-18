using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ProgresLista;

/// <summary>
/// Zaregistruje okno jako Windows AppBar na horním okraji monitoru.
/// Systém pak plochu lišty odečte z pracovní plochy, takže ji maximalizovaná okna nepřekryjí.
/// </summary>
public sealed class AppBar
{
    private readonly Window _window;
    private readonly double _heightDip;
    private IntPtr _hwnd;
    private uint _callbackMessage;
    private bool _registered;

    public AppBar(Window window, double heightDip)
    {
        _window = window;
        _heightDip = heightDip;
    }

    public void Register()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);

        _callbackMessage = RegisterWindowMessage("ProgresListaAppBarMessage");
        var data = NewData();
        data.uCallbackMessage = _callbackMessage;
        SHAppBarMessage(ABM_NEW, ref data);
        _registered = true;

        SetPosition();
    }

    public void Unregister()
    {
        if (!_registered) return;
        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
    }

    private void SetPosition()
    {
        var monitor = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);

        double scale = VisualTreeHelper.GetDpi(_window).DpiScaleY;
        int heightPx = (int)Math.Round(_heightDip * scale);

        var data = NewData();
        data.uEdge = ABE_TOP;
        data.rc = new RECT
        {
            left = info.rcMonitor.left,
            right = info.rcMonitor.right,
            top = info.rcMonitor.top,
            bottom = info.rcMonitor.top + heightPx,
        };

        // Systém může obdélník posunout (např. když je nahoře i hlavní panel).
        SHAppBarMessage(ABM_QUERYPOS, ref data);
        data.rc.bottom = data.rc.top + heightPx;
        SHAppBarMessage(ABM_SETPOS, ref data);

        SetWindowPos(_hwnd, HWND_TOPMOST, data.rc.left, data.rc.top,
            data.rc.right - data.rc.left, data.rc.bottom - data.rc.top, SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_registered) return IntPtr.Zero;

        if (msg == _callbackMessage)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED:
                    SetPosition();
                    break;
                case ABN_FULLSCREENAPP:
                    // Prototyp: lišta zůstává viditelná i přes aplikace na celou obrazovku.
                    SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    break;
            }
            handled = true;
        }
        else if (msg == WM_ACTIVATE)
        {
            var data = NewData();
            SHAppBarMessage(ABM_ACTIVATE, ref data);
        }
        else if (msg == WM_WINDOWPOSCHANGED)
        {
            var data = NewData();
            SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref data);
        }
        else if (msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED)
        {
            _window.Dispatcher.BeginInvoke(SetPosition);
        }
        return IntPtr.Zero;
    }

    private APPBARDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
    };

    // --- Win32 ---

    private const int ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3,
        ABM_ACTIVATE = 6, ABM_WINDOWPOSCHANGED = 9;
    private const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;
    private const int ABE_TOP = 1;
    private const int WM_ACTIVATE = 0x0006, WM_WINDOWPOSCHANGED = 0x0047,
        WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);
}

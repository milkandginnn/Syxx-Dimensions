using System;
using System.IO;
using System.Runtime.InteropServices;
using Syxx.Kit;

namespace SyxxUI.Services;

/// <summary>
/// A6 — the tray trust indicator, Windows twin of the macOS MenuBarExtra (SyxxApp.swift).
///
/// Raw Shell_NotifyIcon + a hidden message window, deliberately dependency-free (the unpackaged
/// WinUI 3 app has no tray control of its own). The icon colour and tooltip come from the SAME
/// Kit-level <see cref="TrayIndicators"/> the in-app grade uses, so the two surfaces cannot
/// disagree — and the icon is grey before any scan and grey on an unmeasured result, NEVER green
/// on a state we did not establish.
///
/// The context menu mirrors the macOS dropdown: status line, latched change + Acknowledge,
/// "Watch this network" (A5 monitoring toggle), Open Syxx, Quit.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int WmTrayCallback = 0x8000 + 1;   // WM_APP + 1
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4;
    private const uint NimAdd = 0x0, NimModify = 0x1, NimDelete = 0x2;
    private const uint WmRButtonUp = 0x205, WmLButtonUp = 0x202, WmCommand = 0x111;

    // Menu command ids.
    private const uint CmdStatus = 1, CmdWatch = 2, CmdAcknowledge = 3, CmdOpen = 4, CmdQuit = 5;

    private readonly WndProcDelegate _wndProc;    // rooted so the GC never collects the thunk
    private IntPtr _hwnd;
    private IntPtr _hicon;
    private string _tooltip = TrayIndicators.NeverScannedTooltip;
    private Severity _severity = Severity.Unknown;
    private bool _added;

    /// State the menu renders; the host keeps these current.
    public Func<bool> IsMonitoringOn = () => false;
    public Func<MonitorTick?> LatchedChange = () => null;
    public Action<bool>? OnWatchToggled;
    public Action? OnAcknowledge;
    public Action? OnOpen;
    public Action? OnQuit;

    public TrayIconService()
    {
        _wndProc = WndProc;
        var cls = new WNDCLASSW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = "SyxxTrayWindow",
            hInstance = GetModuleHandleW(null),
        };
        RegisterClassW(ref cls);
        _hwnd = CreateWindowExW(0, cls.lpszClassName, string.Empty, 0, 0, 0, 0, 0,
                                new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return;   // no tray — the app still works, only the indicator is absent

        _hicon = LoadSeverityIcon(Severity.Unknown);
        var data = MakeData(NifMessage | NifIcon | NifTip);
        _added = Shell_NotifyIconW(NimAdd, ref data);
    }

    /// Update the icon + tooltip from the latest conclusion (null = never scanned → grey).
    public void Update(Conclusion? latest)
    {
        if (!_added) return;
        var ind = TrayIndicators.For(latest);
        _severity = ind.Severity;
        _tooltip = ind.Tooltip.Length > 127 ? ind.Tooltip[..127] : ind.Tooltip;
        var oldIcon = _hicon;
        _hicon = LoadSeverityIcon(ind.Severity);
        var data = MakeData(NifIcon | NifTip);
        Shell_NotifyIconW(NimModify, ref data);
        if (oldIcon != IntPtr.Zero && oldIcon != _hicon) DestroyIcon(oldIcon);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = MakeData(0);
            Shell_NotifyIconW(NimDelete, ref data);
            _added = false;
        }
        if (_hicon != IntPtr.Zero) { DestroyIcon(_hicon); _hicon = IntPtr.Zero; }
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }

    // ── internals ─────────────────────────────────────────────────────────────

    private NOTIFYICONDATAW MakeData(uint flags)
    {
        var d = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = WmTrayCallback,
            hIcon = _hicon,
            szTip = _tooltip,
        };
        return d;
    }

    private static IntPtr LoadSeverityIcon(Severity s)
    {
        // Kit glyph hints are macOS-shaped; on Windows the severity picks a pre-rendered .ico.
        string name = s switch
        {
            Severity.Clean => "tray-clean.ico",
            Severity.Info => "tray-info.ico",
            Severity.Warning => "tray-warning.ico",
            Severity.High => "tray-high.ico",
            Severity.Critical => "tray-critical.ico",
            _ => "tray-unknown.ico",
        };
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray", name);
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray", "tray-unknown.ico");
        return LoadImageW(IntPtr.Zero, path, 1 /* IMAGE_ICON */, 16, 16, 0x10 /* LR_LOADFROMFILE */);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmTrayCallback)
        {
            uint mouse = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouse is WmRButtonUp or WmLButtonUp) ShowMenu();
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        const uint MfString = 0x0, MfGrayed = 0x1, MfChecked = 0x8, MfSeparator = 0x800;

        AppendMenuW(menu, MfString | MfGrayed, CmdStatus, _tooltip);
        var change = LatchedChange();
        if (change is { State: MonitorState.Changed })
        {
            AppendMenuW(menu, MfSeparator, 0, null);
            AppendMenuW(menu, MfString | MfGrayed, CmdStatus, "⚠ " + change.Summary);
            AppendMenuW(menu, MfString, CmdAcknowledge, "Acknowledge");
        }
        AppendMenuW(menu, MfSeparator, 0, null);
        AppendMenuW(menu, MfString | (IsMonitoringOn() ? MfChecked : 0), CmdWatch, "Watch this network");
        AppendMenuW(menu, MfString, CmdOpen, "Open Syxx");
        AppendMenuW(menu, MfString, CmdQuit, "Quit Syxx");

        // Required foreground dance, or the menu will not dismiss on outside clicks.
        SetForegroundWindow(_hwnd);
        GetCursorPos(out var pt);
        uint cmd = (uint)TrackPopupMenuEx(menu, 0x100 /* TPM_RETURNCMD */ | 0x2 /* TPM_RIGHTBUTTON */,
                                          pt.X, pt.Y, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);
        switch (cmd)
        {
            case CmdWatch: OnWatchToggled?.Invoke(!IsMonitoringOn()); break;
            case CmdAcknowledge: OnAcknowledge?.Invoke(); break;
            case CmdOpen: OnOpen?.Invoke(); break;
            case CmdQuit: OnQuit?.Invoke(); break;
        }
    }

    // ── interop ───────────────────────────────────────────────────────────────

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassW(ref WNDCLASSW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImageW(IntPtr inst, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, uint id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpmParams);
}

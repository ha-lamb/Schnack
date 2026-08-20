using System.Runtime.InteropServices;

namespace Schnack.Interop;

internal static class Win32
{
    // ── Foreground-Window-Abfrage ────────────────────────────────────────
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    // ── AttachThreadInput-Trick: macht SetForegroundWindow zuverlässig ───
    // Ohne diesen Trick schlägt SetForegroundWindow fehl, wenn der eigene
    // Prozess nicht aktiv im Vordergrund ist.
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    // ── SendInput: einzige erlaubte Methode für Tastendrücke ─────────────
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // ── Konstanten ────────────────────────────────────────────────────────
    internal const int INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_SCANCODE = 0x0008;
    internal const uint KEYEVENTF_UNICODE = 0x0004;
    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint VK_CONTROL = 0x11;
    internal const byte VK_V = 0x56;
    internal const uint MAPVK_VK_TO_VSC = 0;

    internal const int GWL_EXSTYLE = -20;
    internal const uint WS_EX_NOACTIVATE = 0x0800_0000;
    internal const uint WS_EX_TOOLWINDOW = 0x0000_0080;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    // ── Cursor und Monitor: für die Platzierung des Tray-Kontextmenüs ────
    // SystemParameters.WorkArea liefert nur den Primärmonitor — für ein Menü,
    // das am Cursor aufgeht, brauchen wir den Arbeitsbereich des Monitors dort.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // ── Structs ───────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    // Explicit layout notwendig, damit beide Union-Member an Offset 0 liegen.
    // Falscher Layout → falsche Struct-Größe → SendInput schlägt stillschweigend fehl.
    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    // Wird nicht direkt verwendet, ist aber als größtes Union-Member nötig,
    // damit sizeof(INPUT) dem Win32-Layout entspricht — Entfernen bricht SendInput.
    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }
}

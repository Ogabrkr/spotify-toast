using System.Runtime.InteropServices;
using System.Text;

namespace SpotifyToast;

internal static class Native
{
    public const int WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x00000008;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    public const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }


    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);


    public static float ScaleFor(Rectangle bounds)
    {
        var mon = MonitorFromPoint(new POINT(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), 2);
        try { if (GetDpiForMonitor(mon, 0, out var dx, out _) == 0 && dx > 0) return dx / 96f; } catch { }
        return 1f;
    }

    /// <summary>Jogo, vídeo ou apresentação ocupando a tela inteira?</summary>
    public static bool IsFullscreenActive()
    {
        try
        {
            // 2 = BUSY (app em tela cheia), 3 = D3D exclusivo, 4 = modo apresentação
            if (SHQueryUserNotificationState(out var st) == 0 && st is 2 or 3 or 4) return true;
        }
        catch { }

        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        var sb = new StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        var cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        // janela comum (com barra de título, maximizada ou não) não é tela cheia;
        // jogos em "tela cheia sem bordas" não têm barra de título, mesmo maximizados
        const int WS_CAPTION = 0x00C00000;
        if ((GetWindowLong(fg, -16 /* GWL_STYLE */) & WS_CAPTION) == WS_CAPTION) return false;

        if (!GetWindowRect(fg, out var r)) return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, 2), ref mi)) return false;
        if ((mi.dwFlags & 1 /* MONITORINFOF_PRIMARY */) == 0) return false; // o popup aparece no principal
        var m = mi.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    /// <summary>Cantos arredondados e cor de borda (Windows 11; no 10 é ignorado).</summary>
    public static void RoundCorners(IntPtr hwnd, Color? border = null)
    {
        try
        {
            int pref = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref pref, 4);
            if (border is Color c)
            {
                int bgr = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(hwnd, 34, ref bgr, 4);
            }
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ACCENT_POLICY { public int AccentState, AccentFlags, GradientColor, AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWCOMPOSITIONATTRIBDATA { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);


    /// <summary>
    /// Liquid Glass: o Windows desfoca o que está atrás (blur do DWM, sem cor nem ruído) e o app
    /// desenha por cima a tinta, os reflexos e o aro. Os cantos são os arredondados do Windows 11,
    /// o único recorte que o desfoque respeita (com SetWindowRgn sobrava um quadrado nos cantos).
    /// O app nunca lê a tela.
    /// </summary>
    public static void SetBackdrop(IntPtr hwnd, bool glass, bool dark)
    {
        try
        {
            int round = 2; // ROUND: único recorte que o desfoque do DWM respeita (8 px, suavizado)
            DwmSetWindowAttribute(hwnd, 33, ref round, 4);
            int darkMode = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, 4);
            int border = glass ? unchecked((int)0xFFFFFFFE) : dark ? 0x00403C3A : 0x00DEDBDA;
            DwmSetWindowAttribute(hwnd, 34, ref border, 4);
        }
        catch { }

        try
        {
            var accent = new ACCENT_POLICY { AccentState = glass ? 3 /* blur */ : 0 };
            int size = Marshal.SizeOf<ACCENT_POLICY>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = 19, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { }
    }
}

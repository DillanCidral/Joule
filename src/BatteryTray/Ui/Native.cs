using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BatteryTray.Ui;

/// <summary>
/// Duas familias de janela:
///  - Vidro tingido: transparencia por pixel (WPF AllowsTransparency), a
///    pelicula e os cantos desenhados pelo app. E' o que o TintedGlass do
///    Windhawk vira com "Efeitos de transparencia" desligado -- pelicula
///    preta sem blur -- e funciona com a opcao ligada ou nao. Medido aqui
///    (2026-10-11): com ela desligada, nem o Acrylic do DWM nem o accent
///    policy desfocam; os dois viram cor solida.
///  - Sistema / Solido: janela com moldura, Mica (ou nada) do DWM, que tambem
///    arredonda em 8 px e da a sombra do Windows.
///  - Vidro com desfoque (so com a transparencia ligada): continua por pixel,
///    com uma janela de fundo borrado por baixo (BlurUnderlay). O Acrylic do
///    DWM e o accent policy (2026-11-10) sairam no mesmo dia: o tom escuro fixo
///    do Acrylic somado a pelicula deixava o vidro preto, e a regiao do GDI
///    serrilhava os cantos do widget.
/// </summary>
internal static class Native
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_BORDER_COLOR = 34;

    private const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCACTIVATE = 0x0086;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    /// <summary>
    /// Material da janela COM moldura (Sistema e Solido); o vidro tingido nao
    /// passa por aqui. Chame de novo quando o tema mudar.
    /// </summary>
    public static void ApplyBackdrop(Window window, Appearance appearance, bool dark)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();

        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        int darkFlag = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));

        // Sistema usa a borda do Windows; Solido, a do token.
        int border = unchecked((int)(appearance switch
        {
            Appearance.System => DWMWA_COLOR_DEFAULT,
            _ => dark ? 0x002E2E2Eu : 0x00E0E0E0u, // COLORREF 0x00BBGGRR (cinzas: igual nos dois sentidos)
        }));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));

        int backdrop = appearance switch { Appearance.System => DWMSBT_MAINWINDOW, _ => DWMSBT_NONE };
        bool see = backdrop != DWMSBT_NONE;
        // O material aparece onde o WPF nao pinta: fundo transparente e a
        // moldura do DWM estendida pela janela inteira.
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is not null)
            source.CompositionTarget.BackgroundColor = see ? Colors.Transparent : Colors.Black;

        var margins = see ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
    }

    /// <summary>
    /// O DWM so pinta Acrylic/Mica na janela que ele acha ativa; inativa, cai
    /// para a cor solida de reserva. O painel perde o foco ao abrir pela
    /// bandeja em alguns casos e o widget nunca tem foco -- os dois ficariam
    /// opacos. Responder WM_NCACTIVATE sempre como "ativa" mantem o vidro. Nao
    /// mexe no foco de verdade: o Deactivated do WPF vem de WM_ACTIVATE.
    /// </summary>
    public static void KeepBackdropActive(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg != WM_NCACTIVATE) return IntPtr.Zero;
            handled = true;
            return DefWindowProc(h, msg, new IntPtr(1), lParam);
        });
        SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    /// <summary>
    /// Notificacao do icone da bandeja SEM o som do Windows (NIIF_NOSOUND), para
    /// o Joule tocar o dele. O NotifyIcon do WinForms nao expoe essa opcao: a
    /// janela e o id dele saem por reflexao. Falhou = false, e quem chama usa o
    /// ShowBalloonTip normal. O clique continua chegando ao BalloonTipClicked.
    /// </summary>
    public static bool ShowSilentBalloon(System.Windows.Forms.NotifyIcon icon, string title, string text, bool warning)
    {
        try
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var fields = typeof(System.Windows.Forms.NotifyIcon).GetFields(F);
            var window = fields.Select(f => f.GetValue(icon)).OfType<System.Windows.Forms.NativeWindow>().FirstOrDefault();
            var idField = fields.FirstOrDefault(f => f.FieldType == typeof(uint) && f.Name.Contains("id", StringComparison.OrdinalIgnoreCase));
            if (window is null || window.Handle == IntPtr.Zero || idField is null) return false;

            var data = new NotifyIconData
            {
                Size = Marshal.SizeOf<NotifyIconData>(),
                Window = window.Handle,
                Id = (uint)idField.GetValue(icon)!,
                Flags = 0x10, // NIF_INFO
                Tip = "",
                Info = text.Length > 255 ? text[..255] : text,
                InfoTitle = title.Length > 63 ? title[..63] : title,
                InfoFlags = (warning ? 2u : 1u) | 0x10u, // NIIF_WARNING/NIIF_INFO | NIIF_NOSOUND
                TimeoutOrVersion = 8000,
            };
            return Shell_NotifyIcon(1, ref data); // NIM_MODIFY
        }
        catch (Exception ex)
        {
            Core.Log.Error("BalaoSemSom", ex);
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public int Flags; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

    public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32 { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point32 point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    /// <summary>Algum botao do mouse apertado agora, em qualquer janela.</summary>
    public static bool AnyMouseButtonDown() =>
        GetAsyncKeyState(0x01) < 0 || GetAsyncKeyState(0x02) < 0 || GetAsyncKeyState(0x04) < 0; // esquerdo, direito, meio
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point32 point, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfoEx(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const int MONITOR_DEFAULTTONEAREST = 2;

    // Tudo abaixo em pixels fisicos: o app e' PerMonitorV2, e os DIPs do WPF
    // mudam de tamanho ao cruzar monitores de escala diferente.

    public static (int X, int Y) CursorPos() => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    public static PixelRect WindowRect(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r) ? new PixelRect(r.Left, r.Top, r.Right, r.Bottom) : default;
    }

    public static void MoveWindowTo(Window window, int x, int y)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Posicao e tamanho de uma vez, em pixels.</summary>
    public static void PlaceWindow(Window window, PixelRect r)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Monitor sob um ponto: nome do dispositivo e area de trabalho.</summary>
    public static (string Device, PixelRect Work) MonitorAt(int x, int y) =>
        Describe(MonitorFromPoint(new Point32 { X = x, Y = y }, MONITOR_DEFAULTTONEAREST));

    /// <summary>Area de trabalho do monitor pelo nome; se ele sumiu (desplugado), o principal.</summary>
    public static PixelRect WorkAreaOf(string? device)
    {
        PixelRect? found = null;
        if (!string.IsNullOrEmpty(device))
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, _, _, _) =>
            {
                var d = Describe(m);
                if (string.Equals(d.Device, device, StringComparison.OrdinalIgnoreCase)) { found = d.Work; return false; }
                return true;
            }, IntPtr.Zero);
        }
        return found ?? Describe(MonitorFromPoint(new Point32(), 1 /* PRIMARY */)).Work;
    }

    private static (string Device, PixelRect Work) Describe(IntPtr monitor)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), Device = "" };
        if (!GetMonitorInfoEx(monitor, ref info)) return ("", default);
        return (info.Device ?? "", new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
    }

    /// <summary>
    /// A janela da frente cobre o monitor inteiro (jogo, video em tela cheia,
    /// apresentacao)? O widget se esconde para nao ficar por cima. Area de
    /// trabalho e barra de tarefas nao contam.
    /// </summary>
    /// <param name="onSameMonitorAs">So conta se for no monitor desta janela (jogo numa tela, widget na outra fica).</param>
    public static bool ForegroundIsFullscreen(Window onSameMonitorAs)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        GetWindowThreadProcessId(fg, out int pid);
        if (pid == Environment.ProcessId) return false;

        var cls = new System.Text.StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if (!GetWindowRect(fg, out var r)) return false;
        IntPtr monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        IntPtr mine = new WindowInteropHelper(onSameMonitorAs).Handle;
        if (mine != IntPtr.Zero && MonitorFromWindow(mine, MONITOR_DEFAULTTONEAREST) != monitor) return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        return r.Left <= info.Monitor.Left && r.Top <= info.Monitor.Top
            && r.Right >= info.Monitor.Right && r.Bottom >= info.Monitor.Bottom;
    }

    /// <summary>Fora da barra de tarefas e do Alt+Tab; opcionalmente sem roubar o foco e sem pegar clique.</summary>
    public static void MakeToolWindow(Window window, bool noActivate = false, bool clickThrough = false)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        if (noActivate) ex |= WS_EX_NOACTIVATE;
        if (clickThrough) ex |= WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        // Sem menu de sistema o DWM nao desenha o "x" na moldura de vidro.
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & ~WS_SYSMENU;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier { public int Size; public IntPtr Hwnd; public uint Id; public Guid Item; }

    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier id, out Rect rect);

    /// <summary>
    /// Onde o icone da bandeja esta na barra de tarefas, em pixels; nulo se o
    /// Windows nao disser ou se ele estiver escondido na setinha (o retangulo
    /// cai dentro da area de trabalho, onde a barra nao esta). O NotifyIcon do
    /// WinForms nao expoe janela e id: lidos por reflexao, com fallback nulo.
    /// </summary>
    public static PixelRect? TrayIconRect(System.Windows.Forms.NotifyIcon icon)
    {
        try
        {
            var fields = typeof(System.Windows.Forms.NotifyIcon).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var window = fields.Select(f => f.GetValue(icon)).OfType<System.Windows.Forms.NativeWindow>().FirstOrDefault();
            var idField = fields.FirstOrDefault(f => f.Name.TrimStart('_').Equals("id", StringComparison.OrdinalIgnoreCase));
            if (window is null || window.Handle == IntPtr.Zero || idField is null) return null;

            var nid = new NotifyIconIdentifier
            {
                Size = Marshal.SizeOf<NotifyIconIdentifier>(),
                Hwnd = window.Handle,
                Id = Convert.ToUInt32(idField.GetValue(icon)),
            };
            if (Shell_NotifyIconGetRect(ref nid, out var r) != 0 || r.Right <= r.Left) return null;

            var work = MonitorAt((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2).Work;
            int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
            bool insideWork = cx > work.Left && cx < work.Right && cy > work.Top && cy < work.Bottom;
            return insideWork ? null : new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
        }
        catch (Exception ex) { Core.Log.Error("TrayIconRect", ex); return null; }
    }
}

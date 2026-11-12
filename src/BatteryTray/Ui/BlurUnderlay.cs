using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.UI.Composition;

namespace BatteryTray.Ui;

/// <summary>
/// Fundo desfocado com opacidade regulavel, atras de uma janela por pixel
/// (painel, widget, boas-vindas). Tecnica do token-monitor (MIT,
/// github.com/Javis603/token-monitor, src/windows-dock/DockBackdrop.cs; codigo
/// nosso): uma janela sem bitmap de redirecionamento logo abaixo da janela do
/// app, pintada pelo Windows.UI.Composition com um HostBackdropBrush. O
/// Opacity do visual regula quanto do borrado aparece (o accent policy e o
/// Acrylic do DWM so ligam ou desligam), e o clipe arredondado do Composition
/// tem antisserrilhado (a regiao do GDI, usada antes no widget, nao tinha).
/// So existe com "Efeitos de transparencia" ligado: desligado, o Windows nao
/// pinta o HostBackdrop.
/// </summary>
internal sealed class BlurUnderlay : IDisposable
{
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);

    private const int WM_WINDOWPOSCHANGED = 0x0047, WM_NCHITTEST = 0x0084, WM_MOUSEACTIVATE = 0x0021;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80, SWP_NOOWNERZORDER = 0x200;

    private static IntPtr _queue; // DispatcherQueue da thread da UI: o Compositor exige; vive o app inteiro
    private static Compositor? _compositor;

    private readonly Window _owner;
    private readonly IntPtr _ownerHwnd;
    private readonly HwndSource _host;
    private readonly CompositionTarget _target;
    private readonly ContainerVisual _root;
    private readonly CompositionBrush _brush;
    private IReadOnlyList<Int32Rect>? _shapes = Array.Empty<Int32Rect>();
    private float _radius;

    /// <summary>Cria a camada atras de <paramref name="owner"/> (com handle ja criado); nulo se o Windows nao deixar.</summary>
    public static BlurUnderlay? Attach(Window owner)
    {
        try { return new BlurUnderlay(owner); }
        catch (Exception ex) { Core.Log.Error("BlurUnderlay", ex); return null; }
    }

    private BlurUnderlay(Window owner)
    {
        _owner = owner;
        _ownerHwnd = new WindowInteropHelper(owner).EnsureHandle();

        if (_compositor is null)
        {
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(
                new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 2 }, out _queue)); // thread atual, STA
            _compositor = new Compositor();
        }

        // WS_POPUP; NOREDIRECTIONBITMAP | NOACTIVATE | TOOLWINDOW | TRANSPARENT | TOPMOST:
        // fora do Alt+Tab, da barra e do clique. Sem RootVisual o WPF nao pinta nada nela.
        _host = new HwndSource(new HwndSourceParameters("JouleBlur")
        {
            WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x00200000 | 0x08000000 | 0x80 | 0x20 | 0x8,
            Width = 1,
            Height = 1,
        });
        _host.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg == WM_NCHITTEST) { handled = true; return new IntPtr(-1); } // HTTRANSPARENT
            if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            return IntPtr.Zero;
        });
        int on = 1;
        DwmSetWindowAttribute(_host.Handle, 17, ref on, sizeof(int)); // DWMWA_USE_HOSTBACKDROPBRUSH

        var unknown = ((WinRT.IWinRTObject)_compositor).NativeObject.ThisPtr;
        var interop = (ICompositorDesktopInterop)Marshal.GetObjectForIUnknown(unknown);
        interop.CreateDesktopWindowTarget(_host.Handle, true, out IntPtr raw);
        try { _target = Windows.UI.Composition.Desktop.DesktopWindowTarget.FromAbi(raw); }
        finally { Marshal.Release(raw); }

        _root = _compositor.CreateContainerVisual();
        _brush = _compositor.CreateHostBackdropBrush();
        _target.Root = _root;

        // Segue a janela do app: mover, redimensionar, mostrar, esconder e a ordem na pilha.
        HwndSource.FromHwnd(_ownerHwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool _) =>
        {
            if (msg == WM_WINDOWPOSCHANGED) Sync();
            return IntPtr.Zero;
        });
        owner.Closed += (_, _) => Dispose();
    }

    /// <summary>
    /// Formas desfocadas, em pixels da janela do app (nulo = a janela inteira),
    /// com cantos de <paramref name="radius"/> px; <paramref name="opacity"/> de 0 a 1.
    /// Lista vazia esconde a camada.
    /// </summary>
    public void Update(IReadOnlyList<Int32Rect>? shapes, double radius, double opacity)
    {
        _shapes = shapes;
        _radius = (float)radius;
        _root.Opacity = (float)Math.Clamp(opacity, 0, 1);
        Sync();
    }

    private void Sync()
    {
        if (!GetWindowRect(_ownerHwnd, out var r)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var shapes = _shapes ?? new[] { new Int32Rect(0, 0, w, h) };
        bool show = shapes.Count > 0 && _root.Opacity > 0 && IsWindowVisible(_ownerHwnd) && !IsIconic(_ownerHwnd);

        _root.Children.RemoveAll();
        if (show)
            foreach (var s in shapes)
            {
                var clip = _compositor!.CreateRoundedRectangleGeometry();
                clip.Size = new Vector2(s.Width, s.Height);
                clip.CornerRadius = new Vector2(_radius, _radius);
                var sprite = _compositor.CreateSpriteVisual();
                sprite.Brush = _brush;
                sprite.Offset = new Vector3(s.X, s.Y, 0);
                sprite.Size = new Vector2(s.Width, s.Height);
                sprite.Clip = _compositor.CreateGeometricClip(clip);
                _root.Children.InsertAtTop(sprite);
            }

        // Logo abaixo da janela do app, nunca por cima do texto dela.
        SetWindowPos(_host.Handle, _ownerHwnd, r.Left, r.Top, w, h,
            SWP_NOACTIVATE | SWP_NOOWNERZORDER | (show ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
    }

    public void Dispose()
    {
        _target.Root = null;
        _root.Dispose();
        _brush.Dispose();
        _target.Dispose();
        _host.Dispose();
    }
}

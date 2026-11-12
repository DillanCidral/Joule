using System.Runtime.InteropServices;
using System.Windows.Interop;
using static BatteryTray.Interop.HidNative;

namespace BatteryTray.Ui;

/// <summary>
/// Ouve o Windows avisar que um aparelho entrou ou saiu. NAO toca em
/// aparelho nenhum: e' uma janela escondida recebendo WM_DEVICECHANGE.
///  - HID entrou/saiu (cabo, dongle, Bluetooth HID): caminho da colecao.
///  - "a arvore de aparelhos mudou" (DBT_DEVNODES_CHANGED, so para janela de
///    topo): e' como o Bluetooth de audio aparece; quem ouve rele so o que o
///    Windows ja guardou.
/// O que fazer com o aviso (esperar, ler uma vez) fica no TrayController.
/// Mora em Ui (e nao em Interop) porque usa a janela do WPF: diag e JouleScan compilam o Interop sem WPF.
/// </summary>
public sealed class DeviceWatch : IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_DEVICEINTERFACE = 5;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    private const int WS_POPUP = unchecked((int)0x80000000);

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastInterface
    {
        public int Size, DeviceType, Reserved;
        public Guid ClassGuid;
        public short Name;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref DevBroadcastInterface filter, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    private readonly HwndSource _window;
    private readonly IntPtr _registration;

    /// <summary>Colecao HID que entrou (true) ou saiu (false), pelo caminho.</summary>
    public event Action<string, bool>? HidChanged;

    /// <summary>A arvore de aparelhos mudou (Bluetooth ligou/desligou, entre outros).</summary>
    public event Action? DevicesChanged;

    public DeviceWatch()
    {
        // Janela de topo escondida: DBT_DEVNODES_CHANGED nao chega em janela so de mensagens.
        _window = new HwndSource(new HwndSourceParameters("JouleDeviceWatch") { WindowStyle = WS_POPUP, Width = 0, Height = 0 });
        _window.AddHook(Hook);

        HidD_GetHidGuid(out Guid hid);
        var filter = new DevBroadcastInterface
        {
            Size = Marshal.SizeOf<DevBroadcastInterface>(),
            DeviceType = DBT_DEVTYP_DEVICEINTERFACE,
            ClassGuid = hid,
        };
        _registration = RegisterDeviceNotification(_window.Handle, ref filter, DEVICE_NOTIFY_WINDOW_HANDLE);
        if (_registration == IntPtr.Zero) Core.Log.Write($"DeviceWatch: RegisterDeviceNotification falhou ({Marshal.GetLastWin32Error()}).");
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DEVICECHANGE) return IntPtr.Zero;
        try
        {
            int kind = wParam.ToInt32();
            if (kind == DBT_DEVNODES_CHANGED) DevicesChanged?.Invoke();
            else if ((kind == DBT_DEVICEARRIVAL || kind == DBT_DEVICEREMOVECOMPLETE) && lParam != IntPtr.Zero
                     && Marshal.ReadInt32(lParam, 4) == DBT_DEVTYP_DEVICEINTERFACE)
            {
                // dbcc_name comeca depois de size, devicetype, reserved e o guid (4+4+4+16).
                string? path = Marshal.PtrToStringUni(lParam + 28);
                if (!string.IsNullOrEmpty(path)) HidChanged?.Invoke(path, kind == DBT_DEVICEARRIVAL);
            }
        }
        catch (Exception ex) { Core.Log.Error("DeviceWatch", ex); }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registration != IntPtr.Zero) UnregisterDeviceNotification(_registration);
        _window.Dispose();
    }
}

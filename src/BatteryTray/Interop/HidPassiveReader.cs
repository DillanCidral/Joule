using System.Runtime.InteropServices;
using static BatteryTray.Interop.HidNative;

namespace BatteryTray.Interop;

/// <summary>
/// Escuta PASSIVA de uma colecao HID. NUNCA escreve nada no dispositivo.
///
/// Contrato de seguranca (aprendido na marra: polling agressivo derrubou
/// mouse e headset do barramento USB):
///   - o handle e' aberto UMA vez;
///   - existe UMA thread com um unico laco de ReadFile bloqueante no MESMO handle;
///   - CancelIoEx e' chamado UMA unica vez, no Dispose;
///   - se o ReadFile falhar (dispositivo sumiu), o laco para e nao se toca
///     mais no handle a nao ser para fecha-lo.
/// Nao existe reabrir/cancelar em laco em lugar nenhum.
/// </summary>
public sealed class HidPassiveReader : IDisposable
{
    private readonly Action<byte[]> _onReport;
    private readonly int _reportLength;
    private IntPtr _handle;
    /// <summary>Linux: the hidraw descriptor (-1 on Windows) and the collection, to keep only its reports.</summary>
    private int _fd = -1, _waker = -1;
    private HidCollection? _collection;
    private Thread? _thread;
    private volatile bool _stopping;
    private int _disposed;

    /// <summary>Verdadeiro se o laco terminou porque o dispositivo sumiu / deu erro.</summary>
    public volatile bool DeviceLost;

    /// <summary>Codigo Win32 do erro que encerrou o laco, se houve.</summary>
    public int LastError { get; private set; }

    public string Path { get; }

    // Caminhos com handle de escuta aberto neste processo: a captura do
    // "Mandar meu aparelho" pula esses, para nunca abrir um segundo handle.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> OpenPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ja existe um leitor deste processo escutando este caminho.</summary>
    public static bool IsOpen(string path) => OpenPaths.ContainsKey(path);

    private HidPassiveReader(string path, IntPtr handle, int reportLength, Action<byte[]> onReport)
    {
        Path = path;
        _handle = handle;
        _reportLength = reportLength;
        _onReport = onReport;
    }

    /// <summary>
    /// Abre a colecao para leitura e comeca a escutar. Devolve null se a colecao
    /// e' proibida, nao entrega relatorios de entrada, ou o handle nao abriu.
    /// </summary>
    public static HidPassiveReader? Open(HidCollection collection, Action<byte[]> onReport)
    {
        // Guarda dura: nunca abrir mouse/teclado de sistema para leitura.
        if (HidInventory.IsSystemReserved(collection.UsagePage, collection.Usage))
            return null;

        // Nunca fazer leitura bloqueante em colecao que nao entrega relatorios.
        if (collection.InputLen <= 0)
            return null;

        if (!OperatingSystem.IsWindows())
        {
            int fd = HidLinux.Open(collection.Path, HidLinux.O_RDONLY);
            if (fd < 0) return null;
            int waker = HidLinux.NewWaker();
            if (waker < 0) { HidLinux.close(fd); return null; }
            var linux = new HidPassiveReader(collection.Path, INVALID_HANDLE_VALUE, collection.InputLen, onReport) { _fd = fd, _waker = waker, _collection = collection };
            OpenPaths.AddOrUpdate(collection.Path, 1, (_, n) => n + 1);
            linux.Start();
            return linux;
        }

        IntPtr handle = CreateFile(
            collection.Path,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle == INVALID_HANDLE_VALUE)
            return null;

        // Buffer generoso: se o app do fabricante consumir primeiro, ainda
        // sobra fila para nos. Falha aqui nao e' fatal.
        try { HidD_SetNumInputBuffers(handle, 64); } catch { /* opcional */ }

        var reader = new HidPassiveReader(collection.Path, handle, collection.InputLen, onReport);
        OpenPaths.AddOrUpdate(collection.Path, 1, (_, n) => n + 1);
        reader.Start();
        return reader;
    }

    private void Start()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "hid-listen",
        };
        _thread.Start();
    }

    private void Loop()
    {
        if (_fd >= 0) { LoopLinux(); return; }
        var buffer = new byte[_reportLength];
        while (!_stopping)
        {
            if (!ReadFile(_handle, buffer, buffer.Length, out int got, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                LastError = err;
                // ERROR_OPERATION_ABORTED = nosso proprio CancelIoEx no Dispose.
                DeviceLost = err != ERROR_OPERATION_ABORTED;
                return; // dispositivo sumiu: para de tocar nele.
            }

            // Relatorio HID nunca vem vazio: leitura de 0 bytes e' o aparelho saindo. Parar, senao o laco gira sem esperar.
            if (got <= 0) { DeviceLost = true; return; }

            var report = new byte[got];
            Array.Copy(buffer, report, got);
            try { _onReport(report); }
            catch { /* consumidor nao pode derrubar o laco */ }
        }
    }

    /// <summary>Same contract as the Windows loop: one descriptor, one thread, stops for good when the device goes.</summary>
    private void LoopLinux()
    {
        var buffer = new byte[Math.Max(_reportLength, 64) + 1];
        while (!_stopping)
        {
            int got = HidLinux.Read(_fd, _waker, buffer);
            if (got == 0) continue;
            if (got < 0) { DeviceLost = !_stopping; return; }
            if (HidLinux.AsWindowsReport(_collection!, buffer, got) is not { } report) continue;
            try { _onReport(report); }
            catch { /* consumidor nao pode derrubar o laco */ }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _stopping = true;
        if (_fd >= 0)
        {
            // One wake, like the single CancelIoEx; close only after the thread left poll().
            HidLinux.Wake(_waker);
            _thread?.Join(1500);
            HidLinux.close(_fd);
            HidLinux.close(_waker);
        }
        IntPtr handle = _handle;
        _handle = INVALID_HANDLE_VALUE;

        if (handle != INVALID_HANDLE_VALUE)
        {
            try { CancelIoEx(handle, IntPtr.Zero); } catch { /* handle ja morto */ }
            _thread?.Join(1500);
            try { CloseHandle(handle); } catch { }
        }
        // Contagem: um leitor de pergunta (HidRequest) pode abrir o mesmo caminho por um instante.
        if (OpenPaths.AddOrUpdate(Path, 0, (_, n) => n - 1) <= 0) OpenPaths.TryRemove(Path, out _);
    }
}

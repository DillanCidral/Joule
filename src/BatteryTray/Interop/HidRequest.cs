using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using BatteryTray.Core;
using static BatteryTray.Interop.HidNative;

namespace BatteryTray.Interop;

/// <summary>
/// A UNICA porta de escrita do projeto: manda uma pergunta a um aparelho e
/// espera a resposta. Existe desde a decisao de 2026-11-04 (o app pode
/// perguntar a bateria, so ela, como fazem Solaar/OpenRazer/HeadsetControl).
///
/// Contrato, para nao repetir o polling que derrubou dongles:
///   - nunca abre mouse/teclado de sistema, e so escreve relatorio de saida que a
///     colecao declara (quase sempre vendor; fone HyperX usa a de controle de audio);
///   - pergunta e espera a resposta antes da proxima: nada sai em rajada;
///   - no maximo <see cref="MaxAsks"/> perguntas por leitura, somando todos os
///     aparelhos e a feature da Razer (decidido em 2026-11-10); passou disso, recusa;
///   - quem abre e' um provider dentro de uma leitura sob demanda, e fecha no fim
///     dela (using). Nao existe sessao aberta entre leituras.
/// As respostas chegam pelos HidPassiveReader de sempre (um handle, um CancelIoEx).
///
/// A Razer pergunta por relatorio de feature, em <see cref="Feature"/>: uma
/// pergunta, a resposta lida no mesmo handle, nada de escuta.
/// </summary>
public sealed class HidRequest : IDisposable
{
    /// <summary>Teto de perguntas por leitura, todos os aparelhos juntos. Logitech: 6 slots x (descoberta + bateria) = 12.</summary>
    public const int MaxAsks = 48;

    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(1);

    private readonly List<HidPassiveReader> _readers = new();
    private readonly BlockingCollection<byte[]> _replies = new(256);
    private readonly Dictionary<string, IntPtr> _writers = new(StringComparer.OrdinalIgnoreCase);
    private int _asks;
    private static int _readingAsks;
    private static int _sent;

    /// <summary>Perguntas que chegaram a tentar sair (escrita ou feature) desde que o processo abriu: o --selftest confere por ele que nada passou da porta.</summary>
    internal static int Sent => Volatile.Read(ref _sent);

    /// <summary>Modelos (VID:PID) desligados por relato de problema: a porta recusa tudo para eles, ate com licenca. Vazio em 2026-11-12.</summary>
    internal static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Marcas que recebem pergunta e ja foram conferidas em aparelho real: perguntam sem licenca. Nenhuma em 2026-11-12.</summary>
    internal static readonly HashSet<ushort> Tested = new();

    private static volatile bool _listenOnly;
    private static volatile HashSet<ushort> _allowed = new();
    private static readonly ConcurrentDictionary<ushort, string> _waiting = new();

    /// <summary>Marcas que pediram pergunta sem licenca nesta execucao (VID -> produto): o aviso de licenca le daqui.</summary>
    internal static IReadOnlyDictionary<ushort, string> WaitingConsent => _waiting;

    /// <summary>VID em hex se o aparelho ("VVVV:PPPP") e' de marca esperando licenca; senao null.</summary>
    internal static string? WaitingVid(string hardwareId) =>
        hardwareId.Length >= 4 && ushort.TryParse(hardwareId.AsSpan(0, 4), System.Globalization.NumberStyles.HexNumber, null, out var vid) && _waiting.ContainsKey(vid)
            ? $"{vid:X4}" : null;

    /// <summary>
    /// A leitura sob demanda comeca: o teto de perguntas volta a zero e valem a
    /// chave "So escutar" e as marcas permitidas de agora. So o ReadingService chama.
    /// </summary>
    internal static void NewReading(bool listenOnly, IEnumerable<string> allowedVids)
    {
        _listenOnly = listenOnly;
        _allowed = allowedVids.Select(v => ushort.TryParse(v, System.Globalization.NumberStyles.HexNumber, null, out var vid) ? vid : (ushort?)null)
            .OfType<ushort>().ToHashSet();
        foreach (var vid in _allowed) _waiting.TryRemove(vid, out _);
        Volatile.Write(ref _readingAsks, 0);
    }

    /// <summary>A camada de seguranca (2026-11-12): "So escutar", modelo bloqueado e marca nao testada sem licenca. Recusa antes de contar no teto.</summary>
    private static bool Permitted(HidCollection c)
    {
        if (_listenOnly || Blocked.Contains(c.HardwareId)) return false;
        if (Tested.Contains(c.Vid) || _allowed.Contains(c.Vid)) return true;
        if (_waiting.TryAdd(c.Vid, c.Product)) Log.Write($"Marca {c.Vid:X4} ({c.Product}) sem licenca para perguntar a bateria: nada enviado.");
        return false;
    }

    /// <summary>Conta a pergunta no teto da leitura. Passou do teto: recusa, e o log diz que foi o teto (nao o aparelho dormindo).</summary>
    private static bool OverCap()
    {
        int n = Interlocked.Increment(ref _readingAsks);
        if (n == MaxAsks + 1) Log.Write($"Teto de {MaxAsks} perguntas desta leitura atingido: o que faltava perguntar fica sem resposta.");
        return n > MaxAsks;
    }

    /// <summary>Perguntas feitas nesta sessao (vai para o log).</summary>
    public int Asks => _asks;

    public static bool IsVendor(HidCollection c) =>
        c.UsagePage >= 0xFF00 && CanAsk(c);

    /// <summary>A guarda da porta: qualquer colecao menos mouse/teclado de sistema.</summary>
    public static bool CanAsk(HidCollection c) => !HidInventory.IsSystemReserved(c.UsagePage, c.Usage);

    /// <summary>Escuta as colecoes de resposta. Devolve null se nenhuma abriu.</summary>
    public static HidRequest? Open(IEnumerable<HidCollection> listenOn)
    {
        var session = new HidRequest();
        foreach (var c in listenOn.Where(CanAsk))
        {
            var reader = HidPassiveReader.Open(c, r => session._replies.TryAdd(r));
            if (reader is not null) session._readers.Add(reader);
        }
        if (session._readers.Count == 0)
        {
            session.Dispose();
            return null;
        }
        return session;
    }

    /// <summary>
    /// Escreve <paramref name="request"/> (com o report id no byte 0) em
    /// <paramref name="target"/> e espera ate <paramref name="timeout"/> por um
    /// relatorio que <paramref name="isReply"/> aceite. Null = sem resposta.
    /// </summary>
    public byte[]? Ask(HidCollection target, byte[] request, Func<byte[], bool> isReply, TimeSpan timeout)
    {
        if (!CanAsk(target) || target.OutputLen < request.Length || !Permitted(target)) return null;
        if (OverCap()) return null;
        _asks++;

        while (_replies.TryTake(out _)) { } // resposta velha nao vale para esta pergunta

        if (!Write(target, request)) return null;

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return null;
            if (!_replies.TryTake(out var reply, left)) return null;
            if (isReply(reply)) return reply;
        }
    }

    private bool Write(HidCollection target, byte[] request)
    {
        Interlocked.Increment(ref _sent);
        if (!_writers.TryGetValue(target.Path, out var handle))
        {
            handle = CreateFile(target.Path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE) return false;
            _writers[target.Path] = handle;
        }

        int len = target.OutputLen;
        IntPtr buffer = Marshal.AllocHGlobal(len);
        IntPtr overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        using var done = new ManualResetEvent(false);
        bool driverHoldsMemory = false;
        try
        {
            var zeros = new byte[len];
            Array.Copy(request, zeros, request.Length);
            Marshal.Copy(zeros, 0, buffer, len);
            Marshal.StructureToPtr(new NativeOverlapped { EventHandle = done.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);

            if (WriteFileOverlapped(handle, buffer, len, IntPtr.Zero, overlapped))
                return true;
            if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING)
                return false;

            // Escrita presa: um unico cancelamento, e espera o cancelamento assentar
            // antes de liberar a memoria que o driver ainda enxerga.
            bool finished = done.WaitOne(WriteTimeout);
            if (!finished)
            {
                CancelIoEx(handle, overlapped);
                // O cancelamento tambem tem prazo: driver que nao devolve nao pode prender a leitura (e o semaforo) para sempre.
                if (!done.WaitOne(WriteTimeout))
                {
                    driverHoldsMemory = true;
                    Log.Write($"Escrita em {target.HardwareId} nao voltou nem depois do cancelamento; desisto dela.");
                    return false;
                }
            }
            return GetOverlappedResult(handle, overlapped, out _, true) && finished;
        }
        finally
        {
            // shortcut: se o driver nunca devolveu a escrita, os ~100 bytes ficam sem liberar (ele ainda pode escrever ali); acontece so com driver travado.
            if (!driverHoldsMemory)
            {
                Marshal.FreeHGlobal(overlapped);
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>
    /// Pergunta por relatorio de feature (Razer): UM SetFeature, espera
    /// <paramref name="wait"/>, e le a resposta com GetFeature. Se o aparelho
    /// disser que ainda esta ocupado, le de novo (sem repetir a pergunta) ate
    /// <paramref name="reads"/> vezes. Handle com acesso 0: o Windows deixa
    /// mandar feature ate para a colecao de mouse, e nada e' lido por ReadFile.
    /// </summary>
    public static byte[]? Feature(HidCollection target, byte[] request, TimeSpan wait, Func<byte[], bool> isBusy, int reads = 3)
    {
        if (target.FeatureLen != request.Length || !Permitted(target)) return null;
        if (OverCap()) return null;
        Interlocked.Increment(ref _sent);

        IntPtr handle = CreateFile(target.Path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE) return null;
        try
        {
            if (!HidD_SetFeature(handle, (byte[])request.Clone(), request.Length)) return null;
            for (int i = 0; i < reads; i++)
            {
                Thread.Sleep(wait);
                var reply = new byte[request.Length];
                reply[0] = request[0]; // report id
                if (!HidD_GetFeature(handle, reply, reply.Length)) return null;
                if (!isBusy(reply)) return reply;
            }
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// So LEITURA: um GET_REPORT de feature, nenhuma pergunta antes (Rapoo VT9).
    /// Handle de acesso 0, como no <see cref="Feature"/>.
    /// </summary>
    public static byte[]? ReadFeature(HidCollection target, byte reportId)
    {
        if (target.FeatureLen <= 1 || !Permitted(target)) return null; // nao escreve, mas e' o host pedindo: "So escutar" vale aqui tambem
        IntPtr handle = CreateFile(target.Path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE) return null;
        try
        {
            var buffer = new byte[target.FeatureLen];
            buffer[0] = reportId;
            return HidD_GetFeature(handle, buffer, buffer.Length) ? buffer : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public void Dispose()
    {
        foreach (var r in _readers) r.Dispose();
        _readers.Clear();
        foreach (var h in _writers.Values) CloseHandle(h);
        _writers.Clear();
        _replies.Dispose();
    }
}

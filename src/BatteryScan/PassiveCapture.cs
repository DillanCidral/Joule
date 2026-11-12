using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Scan;

/// <summary>
/// Captura PASSIVA de relatorios HID. E' o passo que realmente decifra
/// protocolo novo: nada e' enviado ao dispositivo, so se escuta o que ja
/// trafega no barramento.
///
/// As mesmas regras de seguranca do app valem aqui, e sao o motivo de este
/// arquivo existir em vez de um laco de leitura improvisado:
///   - nunca abre mouse/teclado reservados pelo sistema (0x01/0x02, 0x01/0x06);
///   - nunca abre colecao sem relatorios de entrada;
///   - um handle por colecao, aberto UMA vez, com UM unico CancelIoEx no fim
///     (quem garante isso e' o HidPassiveReader, o mesmo que o app usa);
///   - numero maximo de handles simultaneos, para nao sair abrindo tudo.
/// Foi polling agressivo que ja derrubou mouse e headset do barramento; escuta
/// passiva com handle unico e' justamente o contrario disso.
/// </summary>
public static class PassiveCapture
{
    private const int MaxHandles = 24;
    private const int MaxSamplesPerGroup = 8;
    private const int MaxReportsPerCollection = 20000;

    public static CaptureInfo Run(
        IReadOnlyList<HidCollection> collections,
        int seconds,
        HashSet<string>? onlyHardwareIds,
        List<string> warnings,
        Action<int, int>? onSecondElapsed = null,
        CancellationToken cancel = default)
    {
        var info = new CaptureInfo { Seconds = seconds };

        var eligible = new List<HidCollection>();
        foreach (var col in collections)
        {
            if (HidInventory.IsSystemReserved(col.UsagePage, col.Usage))
            {
                info.CollectionsSkipped++;
                info.SkippedReasons.Add($"{col.HardwareId} page=0x{col.UsagePage:X4} usage=0x{col.Usage:X4}: reservada pelo sistema");
                continue;
            }

            if (col.InputLen <= 0)
            {
                info.CollectionsSkipped++;
                info.SkippedReasons.Add($"{col.HardwareId} page=0x{col.UsagePage:X4}: sem relatorios de entrada");
                continue;
            }

            if (HidPassiveReader.IsOpen(col.Path))
            {
                info.CollectionsSkipped++;
                info.SkippedReasons.Add($"{col.HardwareId} page=0x{col.UsagePage:X4}: o Joule ja escuta esta colecao");
                continue;
            }

            if (onlyHardwareIds is not null && !onlyHardwareIds.Contains(col.HardwareId))
            {
                info.CollectionsSkipped++;
                continue;
            }

            eligible.Add(col);
        }

        // Se houver mais colecoes do que handles permitidos, as vendor-defined
        // vao primeiro: e' onde os fabricantes escondem bateria.
        var ranked = eligible
            .OrderBy(Interest)
            .ThenBy(c => c.HardwareId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chosen = ranked.Take(MaxHandles).ToList();

        foreach (var dropped in ranked.Skip(MaxHandles))
        {
            info.CollectionsSkipped++;
            info.SkippedReasons.Add($"{dropped.HardwareId} page=0x{dropped.UsagePage:X4}: limite de {MaxHandles} handles");
        }

        var accumulators = new List<Accumulator>();
        var readers = new List<HidPassiveReader>();

        try
        {
            foreach (var col in chosen)
            {
                var acc = new Accumulator(col);
                var reader = HidPassiveReader.Open(col, acc.Add);
                if (reader is null)
                {
                    info.CollectionsSkipped++;
                    info.SkippedReasons.Add($"{col.HardwareId} page=0x{col.UsagePage:X4}: handle nao abriu");
                    continue;
                }

                accumulators.Add(acc);
                readers.Add(reader);
            }

            info.CollectionsOpened = readers.Count;

            for (int elapsed = 0; elapsed < seconds && !cancel.IsCancellationRequested; elapsed++)
            {
                cancel.WaitHandle.WaitOne(1000);
                // Segundo numero: quantos aparelhos ja mandaram algo.
                int talking = accumulators.Where(a => a.Total > 0).Select(a => a.HardwareId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                onSecondElapsed?.Invoke(elapsed + 1, talking);
            }
        }
        finally
        {
            // Um unico Dispose por reader: um CancelIoEx cada, e acabou.
            foreach (var reader in readers)
            {
                try { reader.Dispose(); }
                catch (Exception ex) { warnings.Add($"captura: falha fechando {reader.Path}: {ex.Message}"); }
            }
        }

        foreach (var acc in accumulators) info.Groups.AddRange(acc.Build());

        info.Groups = info.Groups
            .OrderByDescending(g => g.Bytes.Count(b => b.BatteryCandidate))
            .ThenByDescending(g => g.Count)
            .ToList();

        return info;
    }

    /// <summary>Menor = mais interessante. Vendor-defined e paginas de energia na frente.</summary>
    private static int Interest(HidCollection col) => col.UsagePage switch
    {
        >= 0xFF00 => 0,
        0x0085 or 0x0084 or 0x0006 => 1,
        0x000C => 3,
        0x0001 => 4,
        _ => 2,
    };

    private sealed class Accumulator
    {
        private readonly HidCollection _collection;
        private readonly object _gate = new();
        private readonly Dictionary<string, Group> _groups = new(StringComparer.Ordinal);
        private int _total;

        public Accumulator(HidCollection collection) => _collection = collection;

        public string HardwareId => _collection.HardwareId;
        public int Total => Volatile.Read(ref _total);

        public void Add(byte[] report)
        {
            if (report.Length == 0) return;

            lock (_gate)
            {
                if (_total >= MaxReportsPerCollection) return;
                _total++;

                string key = $"{report[0]:X2}/{report.Length}";
                if (!_groups.TryGetValue(key, out var group))
                    _groups[key] = group = new Group(report[0], report.Length);

                group.Add(report);
            }
        }

        public List<CaptureGroup> Build()
        {
            lock (_gate)
            {
                return _groups.Values
                    .OrderByDescending(g => g.Count)
                    .Select(g => g.ToDto(_collection))
                    .ToList();
            }
        }
    }

    private sealed class Group
    {
        private readonly int _reportId;
        private readonly int _length;
        private readonly List<string> _samples = new();
        private readonly byte[] _min;
        private readonly byte[] _max;
        private readonly HashSet<byte>[] _distinct;

        public int Count { get; private set; }

        public Group(byte reportId, int length)
        {
            _reportId = reportId;
            _length = length;
            _min = new byte[length];
            _max = new byte[length];
            _distinct = new HashSet<byte>[length];
            for (int i = 0; i < length; i++)
            {
                _min[i] = 255;
                _max[i] = 0;
                _distinct[i] = new HashSet<byte>();
            }
        }

        public void Add(byte[] report)
        {
            Count++;

            for (int i = 0; i < _length && i < report.Length; i++)
            {
                byte b = report[i];
                if (b < _min[i]) _min[i] = b;
                if (b > _max[i]) _max[i] = b;
                if (_distinct[i].Count < 256) _distinct[i].Add(b);
            }

            string hex = BitConverter.ToString(report).Replace('-', ' ');
            if (_samples.Count < MaxSamplesPerGroup && !_samples.Contains(hex)) _samples.Add(hex);
        }

        public CaptureGroup ToDto(HidCollection col)
        {
            var bytes = new List<CaptureByteStat>();
            for (int i = 0; i < _length; i++)
            {
                var stat = new CaptureByteStat
                {
                    Offset = i,
                    Min = _min[i],
                    Max = _max[i],
                    Distinct = _distinct[i].Count,
                };

                // Cara de percentual: byte que nao e' o report id, ficou parado
                // durante toda a captura e cabe em 1..100. Bateria nao muda em
                // 30 segundos -- e' justamente por ficar constante que ela se
                // parece com isso.
                stat.BatteryCandidate = i > 0 && stat.Constant && stat.Min is >= 1 and <= 100;
                bytes.Add(stat);
            }

            return new CaptureGroup
            {
                HardwareId = col.HardwareId,
                DeviceName = Known.Name(col.Vid, col.Pid) ?? col.Product,
                Path = col.Path,
                UsagePage = $"0x{col.UsagePage:X4}",
                ReportId = _reportId,
                Length = _length,
                Count = Count,
                Samples = _samples,
                Bytes = bytes,
            };
        }
    }
}

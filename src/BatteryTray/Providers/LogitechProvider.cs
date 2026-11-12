using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Aparelhos Logitech pelos receptores Unifying, Bolt, Lightspeed e Nano, via
/// HID++ 2.0. Primeiro provider que PERGUNTA ao aparelho (decisao de
/// 2026-11-04); a escrita passa so pelo <see cref="HidRequest"/>.
///
/// Protocolo (documentado pela Logitech e usado pelo Solaar; nada de codigo
/// copiado, so o formato):
///   pergunta curta = 10 &lt;slot&gt; &lt;indice da feature&gt; &lt;funcao&lt;&lt;4 | swid&gt; p0 p1 p2
///   resposta       = 10|11 &lt;slot&gt; &lt;indice&gt; &lt;funcao|swid&gt; dados...
///   erro 2.0       = .. &lt;slot&gt; FF &lt;indice&gt; &lt;funcao|swid&gt; &lt;codigo&gt;
///   erro 1.0       = 10 &lt;slot&gt; 8F &lt;indice&gt; &lt;funcao|swid&gt; &lt;codigo&gt; (vem do receptor)
/// A feature 0x0000 (raiz, indice 0) traduz o numero de uma feature no indice
/// dela naquele aparelho. Bateria e' a 0x1004 (unificada), a 0x1000 (status)
/// ou a 0x1001 (tensao), nessa preferencia.
///
/// Quanto pergunta, por leitura: em cada slot de 1 a 6, uma pergunta. Na
/// primeira vez que um aparelho responde nesta execucao do app, mais algumas
/// para achar o indice da bateria e o nome; depois fica guardado na memoria e
/// cada leitura volta a ser uma pergunta por slot. Slot vazio ou aparelho
/// dormindo: o receptor responde com erro na hora, sem incomodar o aparelho.
///
/// Sem aparelho Logitech nesta casa: o formato e' o documentado, e quem
/// confirma sao os testadores da beta.
/// </summary>
public sealed class LogitechProvider : IStandaloneProvider
{
    private const ushort Vid = 0x046D;
    private const byte SoftwareId = 0x0D;
    private const int Slots = 6;

    private const ushort FeatureRoot = 0x0000;
    private const ushort FeatureName = 0x0005;
    private const ushort FeatureUnified = 0x1004;
    private const ushort FeatureStatus = 0x1000;
    private const ushort FeatureVoltage = 0x1001;

    // Codigos de erro HID++ 1.0 que o receptor devolve por um slot.
    private const byte ErrInvalidSubId = 0x01;      // aparelho HID++ 1.0
    private const byte ErrConnectFailed = 0x04;
    private const byte ErrUnknownDevice = 0x08;     // slot vazio
    private const byte ErrResourceError = 0x09;     // pareado, mas dormindo/desligado

    private static readonly TimeSpan AskTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>Receptores conhecidos (lista do Solaar, base_usb.py).</summary>
    private static readonly Dictionary<ushort, string> Receivers = new()
    {
        [0xC548] = "Bolt",
        [0xC52B] = "Unifying", [0xC532] = "Unifying",
        [0xC539] = "Lightspeed", [0xC53A] = "Lightspeed", [0xC53D] = "Lightspeed", [0xC53F] = "Lightspeed",
        [0xC541] = "Lightspeed", [0xC545] = "Lightspeed", [0xC547] = "Lightspeed", [0xC54D] = "Lightspeed",
        [0xC52F] = "Nano", [0xC518] = "Nano", [0xC51A] = "Nano", [0xC51B] = "Nano", [0xC521] = "Nano",
        [0xC525] = "Nano", [0xC526] = "Nano", [0xC52E] = "Nano", [0xC531] = "Nano", [0xC534] = "Nano",
        [0xC535] = "Nano", [0xC537] = "Nano",
    };

    /// <summary>O que ja se sabe de cada aparelho nesta execucao, para perguntar so a bateria.</summary>
    private sealed record DeviceInfo(byte BatteryIndex, ushort BatteryFeature, string Name);
    private readonly Dictionary<string, DeviceInfo> _known = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "Logitech";

    public bool Handles(ushort vid, ushort pid) => vid == Vid && (Receivers.ContainsKey(pid) || Headsets.ContainsKey(pid));

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var found = new List<DeviceEntry>();
        found.AddRange(ReadHeadsets(collections));
        var mine = collections.Where(c => c.Vid == Vid && Receivers.ContainsKey(c.Pid) && HidRequest.IsVendor(c)).ToList();
        if (mine.Count == 0) return found;

        // Cada receptor tem uma colecao curta (0xFF00/0x0001, relatorio 0x10).
        // As respostas longas chegam na colecao 0x0002 do mesmo receptor;
        // escuta-se todas, e as perguntas vao uma de cada vez.
        var shorts = mine.Where(c => c.UsagePage == 0xFF00 && c.Usage == 0x0001 && c.OutputLen >= 7)
                         .OrderBy(c => c.Pid).ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        if (shorts.Count == 0) return found;

        using var session = HidRequest.Open(mine);
        if (session is null)
        {
            Log.Write("Logitech: nao abriu nenhuma colecao HID++.");
            return found;
        }

        var ordinalByPid = new Dictionary<ushort, int>();
        foreach (var receiver in shorts)
        {
            int ordinal = ordinalByPid[receiver.Pid] = ordinalByPid.GetValueOrDefault(receiver.Pid) + 1;
            // ponytail: chave por receptor + slot; trocar o receptor de porta nao
            // muda a chave, mas dois receptores iguais trocam de ordem se mudarem de porta.
            string receiverKey = $"logitech:{receiver.Pid:X4}" + (ordinal > 1 ? $"#{ordinal}" : "");
            int before = found.Count;

            for (byte slot = 1; slot <= Slots; slot++)
            {
                var outcome = ReadSlot(session, receiver, slot, $"{receiverKey}:{slot}");
                if (outcome.StopReceiver) break;
                if (outcome.Entry is not null) found.Add(outcome.Entry);
            }

            if (found.Count == before)
            {
                found.Add(new DeviceEntry
                {
                    Key = receiverKey,
                    DisplayName = $"Receptor Logitech {Receivers[receiver.Pid]}",
                    HardwareId = receiver.HardwareId,
                    ProviderName = Name,
                    Reading = BatteryReading.NoData(L.T("nenhum aparelho respondeu; mexa no aparelho e atualize", "no device answered; move the device and refresh")),
                    IsConnected = true,
                    Origin = "Dongle",
                });
            }
        }

        Log.Write($"Logitech: {session.Asks} perguntas.");
        return found;
    }

    private readonly record struct SlotOutcome(DeviceEntry? Entry, bool StopReceiver);

    private SlotOutcome ReadSlot(HidRequest session, HidCollection receiver, byte slot, string key)
    {
        if (!_known.TryGetValue(key, out var known))
        {
            var first = Call(session, receiver, slot, 0x00, 0, Feature(FeatureUnified));
            if (first.Timeout) return new(null, StopReceiver: true);
            if (first.Error10 is ErrUnknownDevice or ErrResourceError or ErrConnectFailed) return new(null, false);
            if (first.Error10 == ErrInvalidSubId)
                return new(Entry(key, receiver, L.T($"Logitech (slot {slot})", $"Logitech (slot {slot})"), BatteryReading.NoData(L.T("aparelho Logitech antigo (HID++ 1.0), ainda sem suporte", "older Logitech device (HID++ 1.0), not supported yet"))), false);
            if (first.Data is null) return new(null, false);

            byte index = first.Data[0];
            ushort feature = FeatureUnified;
            foreach (var next in new[] { FeatureStatus, FeatureVoltage })
            {
                if (index != 0) break;
                var r = Call(session, receiver, slot, 0x00, 0, Feature(next));
                if (r.Data is null) return new(null, false);
                index = r.Data[0];
                feature = next;
            }

            string name = ReadName(session, receiver, slot) ?? L.T($"Logitech (slot {slot})", $"Logitech (slot {slot})");
            if (index == 0)
                return new(Entry(key, receiver, name, BatteryReading.NoData(L.T("o aparelho nao informa bateria por HID++", "the device doesn’t report battery over HID++"))), false);

            known = _known[key] = new DeviceInfo(index, feature, name);
        }

        var reply = Call(session, receiver, slot, known.BatteryIndex, known.BatteryFeature == FeatureUnified ? 1 : 0);
        if (reply.Timeout) return new(null, StopReceiver: true);
        if (reply.Error10 is not null) return new(null, false); // dormiu entre a descoberta e agora
        if (reply.Data is null)
        {
            // Erro 2.0: o indice mudou (re-pareado com outro aparelho). Redescobre na proxima.
            _known.Remove(key);
            return new(null, false);
        }

        var reading = known.BatteryFeature switch
        {
            FeatureUnified => ParseUnified(reply.Data),
            FeatureStatus => ParseStatus(reply.Data),
            _ => ParseVoltage(reply.Data),
        };
        return new(Entry(key, receiver, known.Name, reading), false);
    }

    private string? ReadName(HidRequest session, HidCollection receiver, byte slot)
    {
        var idx = Call(session, receiver, slot, 0x00, 0, Feature(FeatureName));
        if (idx.Data is not { } d || d[0] == 0) return null;
        byte nameIndex = d[0];

        var count = Call(session, receiver, slot, nameIndex, 0);
        if (count.Data is null) return null;
        int length = Math.Min((int)count.Data[0], 64);

        var chars = new List<byte>();
        while (chars.Count < length)
        {
            var part = Call(session, receiver, slot, nameIndex, 1, (byte)chars.Count);
            if (part.Data is null) break;
            var piece = part.Data.TakeWhile(b => b != 0).Take(length - chars.Count).ToList();
            if (piece.Count == 0) break;
            chars.AddRange(piece);
        }
        return CleanName(System.Text.Encoding.UTF8.GetString(chars.ToArray()));
    }

    internal static string? CleanName(string raw)
    {
        string name = raw.Trim();
        if (name.Length == 0) return null;
        return name.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) ? name : $"Logitech {name}";
    }

    private DeviceEntry Entry(string key, HidCollection receiver, string name, BatteryReading reading) => new()
    {
        Key = key,
        DisplayName = name,
        HardwareId = receiver.HardwareId,
        ProviderName = Name,
        Reading = reading,
        IsConnected = true,
        Kind = BatteryTray.Core.Known.BluetoothKind(null, name),
        Origin = "Dongle",
    };

    private static byte[] Feature(ushort id) => new[] { (byte)(id >> 8), (byte)id };

    internal readonly record struct CallResult(byte[]? Data, byte? Error10, byte? Error20, bool Timeout);

    private static CallResult Call(HidRequest session, HidCollection receiver, byte slot, byte index, int function, params byte[] args)
    {
        var request = BuildRequest(slot, index, function, args);
        var reply = session.Ask(receiver, request, r => MatchReply(request, r) is not null, AskTimeout);
        return reply is null ? new CallResult(null, null, null, Timeout: true) : MatchReply(request, reply)!.Value;
    }

    internal static byte[] BuildRequest(byte slot, byte index, int function, params byte[] args)
    {
        var r = new byte[7];
        r[0] = 0x10;
        r[1] = slot;
        r[2] = index;
        r[3] = (byte)((function << 4) | SoftwareId);
        Array.Copy(args, 0, r, 4, Math.Min(args.Length, 3));
        return r;
    }

    /// <summary>
    /// A resposta e' desta pergunta? Devolve os dados (a partir do byte 4) ou
    /// o codigo de erro. Null = relatorio de outra coisa (notificacao, outra pergunta).
    /// </summary>
    internal static CallResult? MatchReply(byte[] request, byte[] r)
    {
        if (r.Length < 7 || (r[0] != 0x10 && r[0] != 0x11) || r[1] != request[1]) return null;
        if (r[2] == request[2] && r[3] == request[3])
            return new CallResult(r[4..], null, null, false);
        if ((r[2] == 0x8F || r[2] == 0xFF) && r[3] == request[2] && r[4] == request[3])
            return r[2] == 0x8F
                ? new CallResult(null, r[5], null, false)
                : new CallResult(null, null, r[5], false);
        return null;
    }

    /// <summary>0x1004, funcao 1: [0] percentual, [1] faixa (8 cheia, 4 boa, 2 baixa, 1 critica), [2] carga.</summary>
    internal static BatteryReading ParseUnified(byte[] d)
    {
        if (d.Length < 3) return BatteryReading.NoData(L.T("resposta curta", "short reply"));
        string? charge = d[2] switch { 1 or 2 => L.T("carregando", "charging"), 3 => L.T("carga completa", "fully charged"), _ => null };
        if (d[0] is > 0 and <= 100) return BatteryReading.Percentage(d[0], chargeText: charge);

        string? level = (d[1] & 0x0F) switch
        {
            >= 8 => "FULL",
            >= 4 => "MEDIUM",
            >= 2 => "LOW",
            1 => "EMPTY",
            _ => null,
        };
        return level is null ? BatteryReading.NoData(L.T("o aparelho nao informou o nivel", "the device didn’t report its level")) : BatteryReading.Coarse(level);
    }

    /// <summary>0x1000, funcao 0: [0] percentual (0 = desconhecido), [2] estado.</summary>
    internal static BatteryReading ParseStatus(byte[] d)
    {
        if (d.Length < 3) return BatteryReading.NoData(L.T("resposta curta", "short reply"));
        if (d[0] is 0 or > 100) return BatteryReading.NoData(L.T("o aparelho nao informou o nivel", "the device didn’t report its level"));
        string? charge = d[2] switch { 1 or 2 or 4 => L.T("carregando", "charging"), 3 => L.T("carga completa", "fully charged"), _ => null };
        return BatteryReading.Percentage(d[0], chargeText: charge);
    }

    /// <summary>0x1001, funcao 0: [0..1] milivolts (big-endian), [2] flags (bit 7 = carregando).</summary>
    internal static BatteryReading ParseVoltage(byte[] d)
    {
        if (d.Length < 3) return BatteryReading.NoData(L.T("resposta curta", "short reply"));
        int mv = (d[0] << 8) | d[1];
        if (mv is < 3000 or > 4500) return BatteryReading.NoData(L.T($"tensao fora da faixa ({mv} mV)", $"voltage out of range ({mv} mV)"));
        string? charge = (d[2] & 0x80) == 0 ? null : (d[2] & 0x03) == 1 ? L.T("carga completa", "fully charged") : L.T("carregando", "charging");
        return BatteryReading.Percentage(PercentFromMillivolts(mv), L.T("estimado pela tensao", "estimated from voltage"), chargeText: charge);
    }

    // ---------------- fones (HeadsetControl, lib/devices/logitech_*.hpp) ----------------
    // Fone Logitech fala HID++ direto (indice FF), com o indice da feature de
    // bateria ja conhecido por modelo. A resposta e' a tensao: [4..5] mV, [6] = 3
    // carregando; [2] = FF e' fone desligado. A curva de cada modelo e' a do
    // HeadsetControl (pontos medidos; aqui ligados por reta, la por spline).

    private sealed record Headset(string Name, byte Index, byte FnSw, ushort Page, ushort Usage, (int Mv, int Pct)[] Curve);

    private static readonly (int, int)[] CurveG533 = { (4200, 100), (3850, 50), (3790, 30), (3750, 20), (3680, 5), (3330, 0) };
    private static readonly (int, int)[] CurveG535 = { (4175, 100), (3817, 50), (3766, 30), (3730, 20), (3664, 5), (3310, 0) };
    private static readonly (int, int)[] CurveG633 = { (4100, 100), (3950, 80), (3850, 60), (3750, 40), (3650, 20), (3500, 10), (3300, 5), (3150, 0) };
    private static readonly (int, int)[] CurveGPro = { (4150, 100), (3830, 50), (3780, 30), (3740, 20), (3670, 5), (3320, 0) };

    private static readonly Dictionary<ushort, Headset> Headsets = new()
    {
        [0x0A66] = new("Logitech G533", 0x07, 0x01, 0xFF43, 0x0202, CurveG533),
        [0x0AC4] = new("Logitech G535", 0x05, 0x0D, 0x000C, 0x0001, CurveG535),
        [0x0A5C] = new("Logitech G633", 0x08, 0x0A, 0xFF43, 0x0202, CurveG633),
        [0x0A89] = new("Logitech G635", 0x08, 0x0A, 0xFF43, 0x0202, CurveG633),
        [0x0A5B] = new("Logitech G933", 0x08, 0x0A, 0xFF43, 0x0202, CurveG633),
        [0x0A87] = new("Logitech G935", 0x08, 0x0A, 0xFF43, 0x0202, CurveG633),
        [0x0AB5] = new("Logitech G733", 0x08, 0x0A, 0xFF43, 0x0202, CurveG633),
        [0x0AFE] = new("Logitech G733", 0x08, 0x0A, 0xFF43, 0x0202, CurveG633),
        [0x0B1F] = new("Logitech G733", 0x08, 0x0A, 0xFF43, 0x0202, CurveG633),
        [0x0AA7] = new("Logitech G PRO", 0x06, 0x0D, 0xFF43, 0x0202, CurveGPro),
        [0x0AAA] = new("Logitech G PRO X", 0x06, 0x0D, 0xFF43, 0x0202, CurveGPro),
        [0x0ABA] = new("Logitech G PRO X", 0x06, 0x0D, 0xFF43, 0x0202, CurveGPro),
        [0x0AFB] = new("Logitech G PRO X 2", 0x06, 0x0D, 0xFF43, 0x0202, CurveGPro),
        [0x0AFC] = new("Logitech G PRO X 2", 0x06, 0x0D, 0xFF43, 0x0202, CurveGPro),
    };

    private IEnumerable<DeviceEntry> ReadHeadsets(IReadOnlyList<HidCollection> collections)
    {
        foreach (var group in collections.Where(c => c.Vid == Vid && Headsets.ContainsKey(c.Pid)).GroupBy(c => c.Pid))
        {
            var spec = Headsets[group.Key];
            var target = group.FirstOrDefault(c => HidRequest.CanAsk(c) && c.UsagePage == spec.Page && c.Usage == spec.Usage
                                                   && c.OutputLen >= 20 && c.InputLen > 0);
            BatteryReading reading;
            if (target is null)
            {
                reading = BatteryReading.NoData(L.T("não achei o canal de bateria deste fone", "couldn’t find this headset’s battery channel"));
            }
            else
            {
                using var session = HidRequest.Open(new[] { target });
                var request = new byte[] { 0x11, 0xFF, spec.Index, spec.FnSw };
                var reply = session?.Ask(target, request, r => r.Length >= 7 && r[0] is 0x10 or 0x11 && r[1] == 0xFF
                                                               && ((r[2] == spec.Index && r[3] == spec.FnSw) || r[2] is 0xFF or 0x8F), AskTimeout);
                reading = reply is null
                    ? BatteryReading.NoData(L.T("não respondeu; ligue o fone e atualize", "didn’t answer; turn the headset on and refresh"))
                    : ParseHeadset(reply, spec.Curve) ?? BatteryReading.NoData(L.T("fone desligado ou longe do receptor; ligue e atualize", "headset off or far from the receiver; turn it on and refresh"));
            }

            yield return new DeviceEntry
            {
                Key = $"logitech:{group.Key:X4}",
                DisplayName = spec.Name,
                HardwareId = BatteryTray.Core.Known.Id(Vid, group.Key),
                ProviderName = Name,
                Reading = reading,
                IsConnected = true,
                Kind = DeviceKind.Headset,
                Origin = "Dongle",
            };
        }
    }

    /// <summary>Null = fone desligado (o receptor responde FF/8F no lugar do indice).</summary>
    internal static BatteryReading? ParseHeadset(byte[] r, (int Mv, int Pct)[] curve)
    {
        if (r[2] is 0xFF or 0x8F) return null;
        int mv = (r[4] << 8) | r[5];
        if (mv < curve[^1].Mv || mv > 4500) return null;
        return BatteryReading.Percentage(Interpolate(mv, curve), L.T("estimado pela tensão", "estimated from voltage"), chargeText: r[6] == 0x03 ? L.T("carregando", "charging") : null);
    }

    internal static int Interpolate(int mv, (int Mv, int Pct)[] curve)
    {
        if (mv >= curve[0].Mv) return curve[0].Pct;
        for (int i = 1; i < curve.Length; i++)
        {
            var (hiMv, hiPct) = curve[i - 1];
            var (loMv, loPct) = curve[i];
            if (mv >= loMv) return (int)Math.Round(loPct + (double)(mv - loMv) * (hiPct - loPct) / (hiMv - loMv));
        }
        return curve[^1].Pct;
    }

    // ponytail: curva media de celula Li-ion; se os testadores virem valor torto,
    // e' esta tabela que se calibra (por modelo, se for o caso).
    private static readonly (int Mv, int Pct)[] Curve =
    {
        (4150, 100), (4050, 90), (3970, 80), (3900, 70), (3840, 60), (3800, 50),
        (3770, 40), (3740, 30), (3710, 20), (3670, 10), (3620, 5), (3500, 0),
    };

    internal static int PercentFromMillivolts(int mv) => Interpolate(mv, Curve);
}

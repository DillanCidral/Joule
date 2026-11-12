using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Corsair (VID 1B1C): mouses e teclados pelo dongle Slipstream, fones Void
/// antigos, fones dos receptores novos (Void Wireless V2, HS80 Max, Virtuoso
/// Max) e Virtuoso XT/SE. Tudo pela porta do <see cref="HidRequest"/>, uma
/// pergunta de cada vez, so perguntas de LEITURA (GET): nada de modo software,
/// nada que mude configuracao.
///
/// O protocolo novo da Corsair ("Bragi") pergunta uma propriedade: 0x0F e' a
/// bateria em decimos de % (0..1000), 0x10 o estado de carga (1 = carregando).
/// Resposta: [origem] [eco do comando] [status, 0 = ok] [valor little-endian].
/// Cada familia embala isso de um jeito:
///   Slipstream  -> 00 &lt;endpoint&gt; 02 0F        (resposta sem report id)
///   receptor V2 -> 00 02 09 02 0F               (antes, um "acorda" do receptor e do fone)
///   Virtuoso XT -> 02 &lt;alvo 09|08&gt; 02 0F       (resposta com report id 01)
/// Os fones Void antigos usam outra coisa: C9 64, nivel em [2] e estado em [4].
///
/// Fontes (so o formato; nada de codigo copiado): HeadsetControl (GPL-3.0) para
/// os fones, OpenLinkHub (GPL-3.0) para o Slipstream e a lista de modelos. Sem
/// Corsair nesta casa: quem confirma sao os testadores.
/// </summary>
public sealed class CorsairProvider : IStandaloneProvider
{
    private const ushort Vid = 0x1B1C;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromMilliseconds(800);

    private const byte Get = 0x02, PropBattery = 0x0F, PropCharge = 0x10;

    // --- Slipstream (OpenLinkHub, devices.go e slipstream/slipstream.go; PIDs em decimal como la) ---
    private static readonly HashSet<ushort> SlipstreamDongles = new() { 7132, 7039, 7078, 11008, 7013, 7041 };

    private static readonly Dictionary<ushort, (string Name, DeviceKind Kind)> Paired = new()
    {
        [7163] = ("Corsair M55 Wireless", DeviceKind.Mouse),
        [7131] = ("Corsair Scimitar Elite Wireless", DeviceKind.Mouse),
        [11042] = ("Corsair Scimitar Elite Wireless SE", DeviceKind.Mouse),
        [7096] = ("Corsair Nightsabre Wireless", DeviceKind.Mouse),
        [7083] = ("Corsair K100 Air Wireless", DeviceKind.Keyboard),
        [6988] = ("Corsair Ironclaw RGB Wireless", DeviceKind.Mouse),
        [11058] = ("Corsair Ironclaw SE Wireless", DeviceKind.Mouse),
        [7038] = ("Corsair Dark Core RGB Pro SE", DeviceKind.Mouse),
        [7040] = ("Corsair Dark Core RGB Pro", DeviceKind.Mouse),
        [7153] = ("Corsair M75 Wireless", DeviceKind.Mouse),
        [11016] = ("Corsair M75 Wireless", DeviceKind.Mouse),
        [7154] = ("Corsair M75 Air Wireless", DeviceKind.Mouse),
        [7006] = ("Corsair Harpoon RGB Wireless", DeviceKind.Mouse),
        [11054] = ("Corsair Harpoon V2 Wireless", DeviceKind.Mouse),
        [7090] = ("Corsair Darkstar RGB Wireless", DeviceKind.Mouse),
        [7093] = ("Corsair M65 RGB Ultra Wireless", DeviceKind.Mouse),
        [7126] = ("Corsair M65 RGB Ultra Wireless", DeviceKind.Mouse),
        [11010] = ("Corsair K70 Core TKL Wireless", DeviceKind.Keyboard),
        [7064] = ("Corsair Sabre RGB Pro Wireless", DeviceKind.Mouse),
        [7094] = ("Corsair K70 Pro Mini Wireless", DeviceKind.Keyboard),
        [11012] = ("Corsair Makr 75", DeviceKind.Keyboard),
        [7022] = ("Corsair K57 RGB Wireless", DeviceKind.Keyboard),
    };

    // --- receptores de fone novos (HeadsetControl corsair_void_v2w; interfaces dos extras pelo OpenLinkHub) ---
    private static readonly Dictionary<ushort, (string Name, int Interface)> V2Receivers = new()
    {
        [0x2A08] = ("Corsair Void Wireless V2", 4),
        [0x2A02] = ("Corsair Virtuoso Max Wireless", 4),
        [0x2A03] = ("Corsair Virtuoso Max Wireless", 4),
        [0x0A97] = ("Corsair HS80 Max Wireless", 4),
        [0x2A0F] = ("Corsair HS80 Max Wireless", 4),
        [0x2A0C] = ("Corsair HS80 Max Wireless", 3),
        [0x2A05] = ("Corsair Void Max Wireless V2", 3),
        [0x2A12] = ("Corsair Void Max Wireless V2", 3),
    };

    // --- Virtuoso XT/SE (HeadsetControl corsair_virtuoso_xt) ---
    private static readonly Dictionary<ushort, (string Name, bool Wired)> Virtuoso = new()
    {
        [0x0A64] = ("Corsair Virtuoso XT", false),
        [0x0A62] = ("Corsair Virtuoso XT", true),
        [0x0A3E] = ("Corsair Virtuoso SE", false),
        [0x0A3D] = ("Corsair Virtuoso SE", true),
    };

    // --- Void antigos (HeadsetControl corsair_void_rich) ---
    private static readonly HashSet<ushort> VoidClassic = new()
    {
        0x1B1C, 0x1B27, 0x0A14, 0x0A16, 0x0A17, 0x0A1D, 0x0A1A, 0x1B2A, 0x1B23,
        0x1B29, 0x0A55, 0x0A51, 0x0A52, 0x0A38, 0x0A4F, 0x0A2B, 0x0A75, 0x0A56,
    };

    public string Name => "Corsair";

    public bool Handles(ushort vid, ushort pid) => vid == Vid &&
        (SlipstreamDongles.Contains(pid) || V2Receivers.ContainsKey(pid) || Virtuoso.ContainsKey(pid) || VoidClassic.Contains(pid));

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var found = new List<DeviceEntry>();
        foreach (var group in collections.Where(c => Handles(c.Vid, c.Pid)).GroupBy(c => c.Pid))
        {
            ushort pid = group.Key;
            var cols = group.ToList();
            try
            {
                if (SlipstreamDongles.Contains(pid)) found.AddRange(ReadSlipstream(pid, cols));
                else if (V2Receivers.TryGetValue(pid, out var v2)) found.Add(Entry($"corsair:{pid:X4}", v2.Name, pid, DeviceKind.Headset, false, ReadV2(cols, v2.Interface)));
                else if (Virtuoso.TryGetValue(pid, out var vx)) found.Add(Entry($"corsair:{pid:X4}", vx.Name, pid, DeviceKind.Headset, vx.Wired, ReadVirtuoso(cols, vx.Wired)));
                else found.Add(Entry($"corsair:{pid:X4}", "Corsair Void", pid, DeviceKind.Headset, false, ReadVoid(cols)));
            }
            catch (IndexOutOfRangeException)
            {
                found.Add(Entry($"corsair:{pid:X4}", $"Corsair ({pid:X4})", pid, DeviceKind.Generic, false, BatteryReading.NoData(L.T("resposta curta demais", "reply too short"))));
            }
            catch (Exception ex)
            {
                Log.Error($"{Name} {pid:X4}", ex);
                found.Add(Entry($"corsair:{pid:X4}", $"Corsair ({pid:X4})", pid, DeviceKind.Generic, false, BatteryReading.Error(ex.Message)));
            }
        }
        return found;
    }

    private static HidCollection? Pick(List<HidCollection> cols, int iface, ushort page = 0) =>
        cols.FirstOrDefault(c => HidRequest.CanAsk(c) && c.OutputLen > 0 && c.InputLen > 0
                                 && (page == 0 || c.UsagePage == page)
                                 && c.Path.Contains($"&mi_{iface:D2}", StringComparison.OrdinalIgnoreCase));

    // ---------------- Slipstream ----------------

    private List<DeviceEntry> ReadSlipstream(ushort dongle, List<HidCollection> cols)
    {
        var result = new List<DeviceEntry>();
        var target = Pick(cols, 1);
        using var session = target is null ? null : HidRequest.Open(new[] { target });
        if (target is null || session is null)
        {
            result.Add(Entry($"corsair:{dongle:X4}", L.T("Receptor Corsair Slipstream", "Corsair Slipstream receiver"), dongle, DeviceKind.Generic, false, BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"))));
            return result;
        }

        byte[]? Send(params byte[] payload)
        {
            var request = new byte[] { 0x00 }.Concat(payload).ToArray();
            var reply = session.Ask(target, request, r => r.Length > 4 && r[0] == 0x00 && r[2] == payload[1], AskTimeout);
            return reply?[1..];
        }

        // Lista do receptor (o "arquivo" 0x24): abre, le, fecha. Sao leituras do proprio receptor.
        Send(0x08, 0x05, 0x01, 0x24);
        Send(0x08, 0x0D, 0x00, 0x24);
        Send(0x08, 0x09, 0x00, 0x24);
        var list = Send(0x08, 0x08, 0x00, 0x24);
        Send(0x08, 0x05, 0x01);

        var paired = list is null ? new List<(ushort Pid, byte Endpoint)>() : ParsePairedList(list);
        if (paired.Count == 0) paired.Add((0, 0x09)); // sem lista: tenta o endpoint de aparelho unico

        foreach (var (pid, endpoint) in paired)
        {
            var (name, kind) = Paired.TryGetValue(pid, out var known) ? known : (L.T("Corsair (sem fio)", "Corsair (wireless)"), DeviceKind.Generic);
            var battery = Send(endpoint, Get, PropBattery);
            var reading = battery is null ? null : ParseBragi(battery, statusAt: 2, valueAt: 3);
            if (reading is null) continue; // desligado/dormindo

            result.Add(Entry($"corsair:{dongle:X4}:{pid}", name, dongle, kind, false, reading));
        }

        if (result.Count == 0)
            result.Add(Entry($"corsair:{dongle:X4}", L.T("Receptor Corsair Slipstream", "Corsair Slipstream receiver"), dongle, DeviceKind.Generic, false,
                BatteryReading.NoData(L.T("nenhum aparelho respondeu; ligue o aparelho e atualize", "no device answered; turn the device on and refresh"))));
        return result;
    }

    /// <summary>
    /// Lista do receptor (sem o report id): [5] quantos, depois por aparelho
    /// vid(2) ?(2) pid(2 LE) tipo idLen id. Endpoint = 08 + tipo, ou 09 se for um so.
    /// </summary>
    internal static List<(ushort Pid, byte Endpoint)> ParsePairedList(byte[] d)
    {
        var list = new List<(ushort, byte)>();
        int count = d[5];
        int pos = 6;
        for (int i = 0; i < count && pos + 8 <= d.Length; i++)
        {
            ushort pid = (ushort)(d[pos + 4] | (d[pos + 5] << 8));
            byte type = d[pos + 6];
            int idLen = d[pos + 7];
            list.Add((pid, (byte)(count == 1 ? 0x09 : 0x08 + type)));
            pos += 8 + idLen;
        }
        return list;
    }

    /// <summary>Resposta Bragi: status ok e valor 1..1000 (decimos de %). Null = sem aparelho.</summary>
    internal static BatteryReading? ParseBragi(byte[] d, int statusAt, int valueAt, bool charging = false)
    {
        if (d[statusAt] != 0x00) return null;
        int tenths = d[valueAt] | (d[valueAt + 1] << 8);
        if (tenths is 0 or > 1000) return null;
        return BatteryReading.Percentage(tenths / 10, chargeText: charging ? L.T("carregando", "charging") : null);
    }

    // ---------------- receptores V2 ----------------

    private static BatteryReading ReadV2(List<HidCollection> cols, int iface)
    {
        var target = Pick(cols, iface);
        if (target is null) return BatteryReading.NoData(L.T("não achei o canal de bateria deste aparelho", "couldn’t find this device’s battery channel"));
        using var session = HidRequest.Open(new[] { target });
        if (session is null) return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));

        byte[]? Send(byte endpoint, byte prop) =>
            session.Ask(target, new byte[] { 0x00, 0x02, endpoint, Get, prop }, r => r.Length > 6, AskTimeout) is { } r ? r[1..] : null;

        // "Acorda" sem modo software: firmware e batida do receptor, depois batida do fone.
        Send(0x08, 0x13);
        Send(0x08, 0x12);
        if (Send(0x09, 0x12) is null) return BatteryReading.NoData(L.T("fone desligado ou longe do receptor; ligue e atualize", "headset off or far from the receiver; turn it on and refresh"));

        var battery = Send(0x09, PropBattery);
        return (battery is null ? null : ParseV2(battery)) ?? BatteryReading.NoData(L.T("o fone não informou a bateria", "the headset didn’t report its battery"));
    }

    /// <summary>Sem report id: nivel em [4..5], decimos de %.</summary>
    internal static BatteryReading? ParseV2(byte[] d)
    {
        int tenths = d[4] | (d[5] << 8);
        return tenths is 0 or > 1000 ? null : BatteryReading.Percentage(tenths / 10);
    }

    // ---------------- Virtuoso XT/SE ----------------

    private static BatteryReading ReadVirtuoso(List<HidCollection> cols, bool wired)
    {
        var target = Pick(cols, 3, 0xFF42);
        if (target is null) return BatteryReading.NoData(L.T("não achei o canal de bateria deste aparelho", "couldn’t find this device’s battery channel"));
        using var session = HidRequest.Open(new[] { target });
        if (session is null) return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));

        byte[]? Send(byte to, byte prop)
        {
            byte from = to == 0x08 ? (byte)0x00 : (byte)0x01;
            return session.Ask(target, new byte[] { 0x02, to, Get, prop }, r => r.Length > 7 && r[0] == 0x01 && r[1] == from && r[2] == Get, AskTimeout);
        }

        // So o XT no cabo responde por si (08); o resto responde pelo fone (09).
        foreach (byte to in wired ? new byte[] { 0x08, 0x09 } : new byte[] { 0x09, 0x08 })
        {
            var battery = Send(to, PropBattery);
            if (battery is null || battery[3] != 0x00) continue;
            var charge = Send(to, PropCharge);
            bool charging = charge is not null && charge[3] == 0x00 && charge[4] == 0x01;
            var reading = ParseBragi(battery, statusAt: 3, valueAt: 4, charging);
            if (reading is not null) return reading;
        }
        return BatteryReading.NoData(L.T("fone desligado ou longe do receptor; ligue e atualize", "headset off or far from the receiver; turn it on and refresh"));
    }

    // ---------------- Void antigos ----------------

    private static BatteryReading ReadVoid(List<HidCollection> cols)
    {
        var target = Pick(cols, 3, 0xFFC5);
        if (target is null) return BatteryReading.NoData(L.T("não achei o canal de bateria deste aparelho", "couldn’t find this device’s battery channel"));
        using var session = HidRequest.Open(new[] { target });
        if (session is null) return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));

        var r = session.Ask(target, new byte[] { 0xC9, 0x64 }, r => r.Length >= 5, AskTimeout);
        if (r is null) return BatteryReading.NoData(L.T("não respondeu; ligue o fone e atualize", "didn’t answer; turn the headset on and refresh"));
        return ParseVoid(r) ?? BatteryReading.NoData(L.T("fone desligado ou longe do receptor; ligue e atualize", "headset off or far from the receiver; turn it on and refresh"));
    }

    /// <summary>[2] nivel (bit 7 = microfone levantado), [4] estado: 0 sem fone, 1/2 normal/baixa, 4/5 carregando.</summary>
    internal static BatteryReading? ParseVoid(byte[] r)
    {
        byte status = r[4];
        if (status is not (1 or 2 or 4 or 5)) return null;
        if ((r[2] & 0x7F) > 100) return ChineseBrandsProvider.OutOfRange; // regra 6: valor sem sentido nao vira 100%
        return BatteryReading.Percentage(r[2] & 0x7F, chargeText: status is 4 or 5 ? L.T("carregando", "charging") : null);
    }

    private DeviceEntry Entry(string key, string name, ushort pid, DeviceKind kind, bool wired, BatteryReading reading) => new()
    {
        Key = key,
        DisplayName = name,
        HardwareId = BatteryTray.Core.Known.Id(Vid, pid),
        ProviderName = Name,
        Reading = reading,
        IsConnected = true,
        Kind = kind,
        Origin = wired ? "Cabo" : "Dongle",
    };
}

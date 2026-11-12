using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Bateria de controles via XInputGetBatteryInformation. Somente leitura.
///
/// Dois cuidados aprendidos no hardware real:
///  - BatteryType == WIRED e' DESCARTADO. O dongle do 8BitDo Ultimate 2C emula
///    um Xbox 360 com fio e reporta WIRED/FULL de forma constante; e' valor
///    falso, nao bateria. Melhor nao mostrar nada do que mostrar errado.
///  - o sufixo IG_nn do InstanceId NAO e' o indice do slot XInput (dois
///    controles diferentes apareciam como IG_00). Para saber qual VID:PID esta
///    em cada slot usamos XInputGetCapabilitiesEx (ordinal 108).
///
/// Cabo, medido em 2026-10-11:
///  - 8BitDo no cabo e pelo dongle e' o mesmo 2DC8:310A com WIRED. A unica
///    prova de cabo e' o dongle plugado aparecer ocioso como 2DC8:301C
///    ("IDLE"): ocioso, ele nao esta falando com controle nenhum.
///  - Xbox no cabo vira 045E:02FF (pelo adaptador e' 045E:0B12 com
///    ALKALINE/FULL). Logo depois de plugar diz DISCONNECTED, segundos depois
///    WIRED; nos dois casos e' o cabo. A prova e' o PID 02FF, nao o VID: dongle
///    generico se passa por Xbox 360 com fio (045E:028E) e diz WIRED tambem.
/// </summary>
public sealed class XInputProvider : IStandaloneProvider
{
    private const ushort Vid8BitDo = 0x2DC8;
    private const ushort Pid8BitDo = 0x310A;
    private const ushort Pid8BitDoIdleDongle = 0x301C;
    private const ushort VidMicrosoft = 0x045E;
    private const ushort PidXboxCable = 0x02FF;
    private const ushort PidXboxAdapter = 0x0B12; // o XInput informa o controle, nao o adaptador 02E6

    public string Name => "XInput";

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        // ponytail: um dongle ocioso prova o cabo de "um" 8BitDo; com dois dongles
        // (um ativo, um ocioso) o ativo seria lido como cabo. Casar pelo numero
        // de serie se isso um dia existir.
        bool idleDongle = collections.Any(c => c.Vid == Vid8BitDo && c.Pid == Pid8BitDoIdleDongle);

        var found = new List<DeviceEntry>();

        if (!XInputNative.IsAvailable())
        {
            Log.Write("XInput indisponivel nesta maquina.");
            return found;
        }

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (uint slot = 0; slot < 4; slot++)
        {
            uint stateResult = XInputNative.GetState(slot, out _);
            if (stateResult != XInputNative.ERROR_SUCCESS) continue; // slot vazio

            var caps = XInputNative.GetCapabilitiesEx(slot);
            ushort vid = caps?.Vid ?? 0;
            ushort pid = caps?.Pid ?? 0;
            byte subType = caps?.SubType ?? 1;

            string hardwareId = (vid != 0 || pid != 0) ? Known.Id(vid, pid) : "";
            string key = hardwareId.Length > 0 ? $"xinput:{hardwareId}" : $"xinput:slot{slot}";
            if (!usedKeys.Add(key))
            {
                key = $"{key}#slot{slot}";
                usedKeys.Add(key);
            }

            string display = Known.Name(vid, pid)
                             ?? $"{XInputNative.SubTypeName(subType)} (slot {slot + 1})";

            var reading = ReadSlot(slot, vid, pid, idleDongle);
            found.Add(new DeviceEntry
            {
                Key = key,
                DisplayName = display,
                HardwareId = hardwareId,
                ProviderName = Name,
                Reading = reading,
                IsConnected = true,
                Kind = DeviceKind.Gamepad,
                Origin = reading.Status == ReadingStatus.Charging ? "Cabo"
                       : vid == VidMicrosoft && pid == PidXboxAdapter ? "Adaptador"
                       : "Dongle",
            });
        }

        return found;
    }

    private static BatteryReading ReadSlot(uint slot, ushort vid, ushort pid, bool idleDongle)
    {
        uint result = XInputNative.GetBatteryInformation(slot, XInputNative.BATTERY_DEVTYPE_GAMEPAD, out var info);
        if (result != XInputNative.ERROR_SUCCESS)
            return BatteryReading.NoData(L.T($"XInputGetBatteryInformation retornou {result}", $"XInputGetBatteryInformation returned {result}"));

        return MapBattery(info.BatteryType, info.BatteryLevel, vid, pid, idleDongle);
    }

    /// <summary>
    /// Traducao pura de (BatteryType, BatteryLevel) para leitura. Separada do
    /// P/Invoke justamente porque e' aqui que mora a regra de descartar WIRED.
    /// idleDongle: o dongle do 8BitDo esta plugado e ocioso (2DC8:301C presente).
    /// </summary>
    internal static BatteryReading MapBattery(byte batteryType, byte batteryLevel, ushort vid, ushort pid, bool idleDongle = false)
    {
        bool xboxCable = vid == VidMicrosoft && pid == PidXboxCable;

        switch (batteryType)
        {
            case XInputNative.BATTERY_TYPE_WIRED:
                if (vid == Vid8BitDo && pid == Pid8BitDo && idleDongle)
                    return BatteryReading.Charging(L.T("carregando; o dongle esta ocioso, entao este e' o cabo", "charging; the dongle is idle, so this is the cable"));
                // Pilha AA nao carrega: o detalhe nao promete "carregando".
                if (xboxCable)
                    return BatteryReading.Charging(L.T("o XInput nao informa o nivel no cabo", "XInput doesn’t report the level when wired"));
                // Sem prova de cabo, WIRED e' o valor falso do dongle: descartado de proposito.
                return BatteryReading.NoData(L.T("o dongle se apresenta como controle com fio; o XInput não informa bateria real", "the dongle presents itself as a wired controller; XInput doesn’t report the real battery"));

            case XInputNative.BATTERY_TYPE_DISCONNECTED:
                if (xboxCable)
                    return BatteryReading.Charging(L.T("o XInput nao informa o nivel no cabo", "XInput doesn’t report the level when wired"));
                return BatteryReading.NoData("XInput reporta bateria desconectada");

            case XInputNative.BATTERY_TYPE_ALKALINE:
            case XInputNative.BATTERY_TYPE_NIMH:
                string level = batteryLevel switch
                {
                    0 => "EMPTY",
                    1 => "LOW",
                    2 => "MEDIUM",
                    3 => "FULL",
                    _ => "",
                };
                if (level.Length == 0)
                    return BatteryReading.NoData($"nivel desconhecido 0x{batteryLevel:X2}");

                string kind = batteryType == XInputNative.BATTERY_TYPE_ALKALINE ? "alcalina" : "NiMH";
                return BatteryReading.Coarse(level, L.T($"{kind}, XInput so da 4 faixas", $"{kind}, XInput only gives 4 bands"));

            default:
                string? reason = Known.DeadEndReason(vid, pid);
                return BatteryReading.NoData(reason ?? L.T($"tipo de bateria {XInputNative.BatteryTypeName(batteryType)}", $"battery type {XInputNative.BatteryTypeName(batteryType)}"));
        }
    }
}

using BatteryTray.Core;
using BatteryTray.Interop;
using BatteryTray.Providers;

namespace BatteryTray.Scan;

/// <summary>
/// Os 4 slots XInput, crus. Diferente do app, aqui o WIRED NAO e' descartado:
/// e' registrado com a interpretacao do lado, justamente para eu ver quantos
/// dongles mentem "com fio" em outras maquinas.
/// </summary>
public static class XInputScan
{
    public static List<XInputSlotInfo> Collect(List<string> warnings)
    {
        var slots = new List<XInputSlotInfo>();

        try
        {
            if (!XInputNative.IsAvailable())
            {
                warnings.Add("XInput indisponivel nesta maquina.");
                return slots;
            }

            for (uint slot = 0; slot < 4; slot++)
            {
                uint state = XInputNative.GetState(slot, out _);
                if (state != XInputNative.ERROR_SUCCESS)
                {
                    slots.Add(new XInputSlotInfo { Slot = (int)slot, Connected = false });
                    continue;
                }

                var caps = XInputNative.GetCapabilitiesEx(slot);
                ushort vid = caps?.Vid ?? 0;
                ushort pid = caps?.Pid ?? 0;

                uint ret = XInputNative.GetBatteryInformation(slot, XInputNative.BATTERY_DEVTYPE_GAMEPAD, out var battery);
                var mapped = XInputProvider.MapBattery(battery.BatteryType, battery.BatteryLevel, vid, pid);

                slots.Add(new XInputSlotInfo
                {
                    Slot = (int)slot,
                    Connected = true,
                    HardwareId = (vid != 0 || pid != 0) ? Known.Id(vid, pid) : "",
                    SubType = XInputNative.SubTypeName(caps?.SubType ?? 0),
                    BatteryType = ret == XInputNative.ERROR_SUCCESS
                        ? XInputNative.BatteryTypeName(battery.BatteryType)
                        : $"erro {ret}",
                    BatteryLevel = battery.BatteryLevel,
                    Interpretation = mapped.Status == ReadingStatus.Ok
                        ? $"OK: {mapped.Level ?? mapped.Percent?.ToString() ?? "?"}"
                        : $"{mapped.Status}: {mapped.Detail}",
                });
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"XInput: {ex.Message}");
        }

        return slots;
    }
}

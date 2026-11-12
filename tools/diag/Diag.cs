using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Diag;

internal static class Diag
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool verbose = args.Contains("-v");
        int listenSeconds = 0;
        int idx = Array.FindIndex(args, a => a == "--listen");
        if (idx >= 0 && idx + 1 < args.Length) int.TryParse(args[idx + 1], out listenSeconds);

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Contains("--selftest")) return SelfTest.Run();
        if (args.Contains("--autostart-test")) return SelfTest.RunAutostartRoundTrip();

        int feature = Array.FindIndex(args, a => a == "--feature");
        if (feature >= 0)
        {
            if (feature + 2 >= args.Length || !int.TryParse(args[feature + 2], out int reportId))
            {
                Console.WriteLine("uso: --feature <VID:PID em hex> <report id>");
                return 1;
            }
            return FeatureProbe.Run(args[feature + 1], reportId);
        }

        Console.WriteLine("=== inventario HID (somente metadados, handle com acesso 0) ===");
        var collections = HidInventory.Scan();
        foreach (var group in collections.GroupBy(c => c.HardwareId).OrderBy(g => g.Key))
        {
            var first = group.First();
            string name = Known.Name(first.Vid, first.Pid) ?? first.Product;
            Console.WriteLine($"  {group.Key}  {name}  ({group.Count()} colecoes)");
            if (verbose)
            {
                foreach (var c in group.OrderBy(c => c.UsagePage).ThenBy(c => c.Usage))
                {
                    string reserved = HidInventory.IsSystemReserved(c.UsagePage, c.Usage) ? "  [reservada pelo sistema]" : "";
                    Console.WriteLine($"      page=0x{c.UsagePage:X4} usage=0x{c.Usage:X4}  in={c.InputLen} out={c.OutputLen} feat={c.FeatureLen}{reserved}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== XInput ===");
        if (!XInputNative.IsAvailable())
        {
            Console.WriteLine("  XInput indisponivel.");
        }
        else
        {
            Console.WriteLine($"  dll: {XInputNative.DllInUse}");
            for (uint slot = 0; slot < 4; slot++)
            {
                uint st = XInputNative.GetState(slot, out _);
                if (st != XInputNative.ERROR_SUCCESS)
                {
                    Console.WriteLine($"  slot {slot}: vazio (ret={st})");
                    continue;
                }
                var caps = XInputNative.GetCapabilitiesEx(slot);
                uint rb = XInputNative.GetBatteryInformation(slot, XInputNative.BATTERY_DEVTYPE_GAMEPAD, out var bi);
                string ids = caps is null ? "capsEx indisponivel" : $"{caps.Value.Vid:X4}:{caps.Value.Pid:X4} subtype={caps.Value.SubType}";
                Console.WriteLine($"  slot {slot}: CONECTADO  {ids}  battery(ret={rb}) type={XInputNative.BatteryTypeName(bi.BatteryType)} level={bi.BatteryLevel}");
            }
        }

        var registry = ProviderRegistry.CreateDefault();
        var cache = ReadingCache.Load();
        var settings = Settings.Load();
        using var service = new ReadingService(registry, cache, settings);

        service.EnsurePassiveListeners();

        if (listenSeconds > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"=== escuta passiva por {listenSeconds}s (para o Rapoo, que so responde ao RapooOfficeDev.exe) ===");
            Thread.Sleep(listenSeconds * 1000);
        }

        Console.WriteLine();
        Console.WriteLine("=== leitura ===");
        var result = service.Refresh();
        if (result.FatalError is not null) Console.WriteLine($"  FALHA: {result.FatalError}");

        foreach (var d in result.Devices)
        {
            string value = d.Reading.Status == ReadingStatus.Charging ? "carregando"
                : d.EffectivePercent is int p ? $"{p}%"
                : d.EffectiveLevel is { Length: > 0 } lvl ? Known.LevelLabelPtBr(lvl)
                : "--";

            string age = d.Cache?.HasValue == true ? $"  (cache {ReadingCache.DescribeAge(d.Cache.Age)})" : "";
            Console.WriteLine($"  [{d.Reading.Status,-12}] {value,-11} {d.DisplayName,-38} {d.HardwareId,-12} {d.ProviderName}{age}");
            if (verbose && !string.IsNullOrWhiteSpace(d.Reading.Detail))
                Console.WriteLine($"                 -> {d.Reading.Detail}");
        }

        Console.WriteLine();
        Console.WriteLine($"log: {AppPaths.LogFile}");
        return 0;
    }
}

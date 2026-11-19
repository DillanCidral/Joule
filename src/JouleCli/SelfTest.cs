using BatteryTray.Core;
using BatteryTray.Interop;
using BatteryTray.Providers;

namespace BatteryTray.Cli;

/// <summary>Checks of the Linux pieces that need no hardware: descriptor parser, report shape, sysfs values.</summary>
internal static class SelfTest
{
    private static int _failed;

    public static int Run()
    {
        Log.Silent = true;
        L.Set("en");

        // Mouse (report id 1) and vendor collection (report id 3) in one hidraw node, as 2.4 GHz dongles do.
        var composite = HidLinux.ParseDescriptor(B(
            0x05, 0x01, 0x09, 0x02, 0xA1, 0x01, 0x85, 0x01, 0x09, 0x01, 0xA1, 0x00, 0x05, 0x09, 0x19, 0x01, 0x29, 0x05,
            0x15, 0x00, 0x25, 0x01, 0x95, 0x05, 0x75, 0x01, 0x81, 0x02, 0x95, 0x01, 0x75, 0x03, 0x81, 0x01, 0x05, 0x01,
            0x09, 0x30, 0x09, 0x31, 0x16, 0x01, 0x80, 0x26, 0xFF, 0x7F, 0x75, 0x10, 0x95, 0x02, 0x81, 0x06, 0xC0, 0xC0,
            0x06, 0x00, 0xFF, 0x09, 0x01, 0xA1, 0x01, 0x85, 0x03, 0x15, 0x00, 0x26, 0xFF, 0x00, 0x75, 0x08, 0x95, 0x10,
            0x09, 0x01, 0x81, 0x02, 0x95, 0x10, 0x09, 0x01, 0x91, 0x02, 0xC0));
        Check("two top level collections", composite.Count == 2);
        Check("mouse: page 1 usage 2, 5 bytes + id", composite[0] is { Page: 0x01, Usage: 0x02, InputLen: 6, OutputLen: 0, FeatureLen: 0 } && composite[0].InputIds.SequenceEqual(B(1)));
        Check("vendor: page FF00, 16 bytes + id in and out", composite[1] is { Page: 0xFF00, Usage: 0x01, InputLen: 17, OutputLen: 17 } && composite[1].InputIds.SequenceEqual(B(3)));

        var plain = HidLinux.ParseDescriptor(B(0x06, 0x00, 0xFF, 0x09, 0x01, 0xA1, 0x01, 0x75, 0x08, 0x95, 0x20, 0x09, 0x01, 0x81, 0x02, 0xC0));
        Check("unnumbered: 32 bytes + the 0 Windows adds, no ids", plain.Count == 1 && plain[0].InputLen == 33 && plain[0].InputIds.Length == 0);

        var extended = HidLinux.ParseDescriptor(B(0x0B, 0x01, 0x00, 0x0C, 0x00, 0xA1, 0x01, 0x75, 0x08, 0x95, 0x02, 0x81, 0x02, 0xC0));
        Check("4-byte usage carries its page", extended.Count == 1 && extended[0] is { Page: 0x0C, Usage: 0x01 });
        Check("truncated descriptor does not throw", HidLinux.ParseDescriptor(B(0x05, 0x01, 0x09, 0x02, 0xA1, 0x01, 0x85)).Count == 0);

        var vendor = new HidCollection { Path = "/dev/hidraw9", UsagePage = 0xFF00, InputLen = 17, ReportIds = B(3) };
        var own = new byte[64]; own[0] = 3; own[1] = 0x42;
        Check("own report kept, padded to InputLen", HidLinux.AsWindowsReport(vendor, own, 9) is { Length: 17 } r && r[0] == 3 && r[1] == 0x42);
        var mouse = new byte[64]; mouse[0] = 1;
        Check("other collection's report dropped", HidLinux.AsWindowsReport(vendor, mouse, 6) is null);
        var single = new HidCollection { Path = "/dev/hidraw9", InputLen = 33 };
        var raw = new byte[64]; raw[0] = 0x55;
        Check("unnumbered report gets the 0 id in front", HidLinux.AsWindowsReport(single, raw, 32) is { Length: 33 } u && u[0] == 0 && u[1] == 0x55);
        Check("path tag removed before open", HidLinux.Node("/dev/hidraw3#&mi_02") == "/dev/hidraw3");

        Check("sysfs: capacity and charging", PowerSupplyProvider.ToReading("57", null, "Charging") is { Status: ReadingStatus.Ok, Percent: 57, ChargeText: "charging" });
        Check("sysfs: level only", PowerSupplyProvider.ToReading(null, "Low", "Discharging") is { Status: ReadingStatus.Ok, Level: "LOW", Percent: null });
        Check("sysfs: nothing known is no data, not 0%", PowerSupplyProvider.ToReading(null, "Unknown", "Discharging") is { Status: ReadingStatus.NoData, Percent: null });
        Check("sysfs: out of range is no data", PowerSupplyProvider.ToReading("150", null, null) is { Status: ReadingStatus.NoData });

        Console.WriteLine(_failed == 0 ? "self-test: all passed" : $"self-test: {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static byte[] B(params byte[] b) => b;

    private static void Check(string name, bool ok)
    {
        if (!ok) _failed++;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}");
    }
}

# Adding a device

[Português](adicionar-um-aparelho.md)

A device is supported by a **provider**: one class that knows which USB IDs it
answers for and how to turn a report into a battery reading. Nothing else in
the app needs to know it exists.

Read [SECURITY.md](../SECURITY.md) first. Its rules are not negotiable.

## 1. Find out how the device reports battery

- **It already shows in Windows** (*Settings > Bluetooth*): nothing to do, the
  Bluetooth provider reads it.
- **An open project documents it** (Solaar, OpenRazer, HeadsetControl,
  OpenMouse, rivalcfg...): take the **format** only, write the code yourself,
  and name the source in a comment. Before copying data such as model tables,
  check the license.
- **Nobody documents it**: capture it. Run the collector while using the
  device and look for a report whose byte changes with the battery:

```bash
dotnet run --project src/BatteryScan -c Release
```

The output contains serial numbers and Bluetooth addresses. Do not commit it.

## 2. Pick the gentlest way that works

| The device... | Use | Example |
|---|---|---|
| sends battery by itself, or when the manufacturer software asks | `PassiveReportListener` (listen only) | `RapooProvider`, `AttackSharkProvider` |
| streams reports constantly | `HidOneShot.WaitForReport` (open, read one, close) | `DualSenseProvider` |
| only answers a question | `HidRequest` (the single write door) | `LogitechProvider`, `SteelSeriesProvider` |
| is not a HID collection (XInput slot, Windows property) | `IStandaloneProvider` | `XInputProvider`, `BluetoothPnpProvider` |

If the protocol needs the question repeated until two answers match, stop: it
stays out.

## 3. Write the provider

In `src/BatteryTray/Providers/`. A listen-only provider is this small:

```csharp
public sealed class AcmeProvider : IBatteryProvider, IPassiveListenerProvider, IDisposable
{
    private const ushort Vid = 0x1234, Pid = 0x5678;

    private readonly PassiveReportListener _listener = new(
        "Acme",
        c => c.Vid == Vid && c.Pid == Pid && c.UsagePage == 0xFF00 && c.InputLen == 7,
        ParsePercent);

    public string Name => "Acme";
    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new[] { (Vid, Pid) };
    public string LogicalKey(ushort vid, ushort pid) => "acme:m1";
    public string DisplayName(ReadContext ctx) => "Acme M1";

    public void EnsureListening(IReadOnlyList<HidCollection> collections) => _listener.Ensure(collections);
    public void StopListening() => _listener.Stop();
    public bool IsListening => _listener.IsListening;
    public event Action? ValueChanged
    {
        add => _listener.ValueChanged += value;
        remove => _listener.ValueChanged -= value;
    }
    public void Dispose() => _listener.Dispose();

    public BatteryReading Read(ReadContext ctx)
    {
        if (!ctx.IsPresent(Vid, Pid)) return BatteryReading.Disconnected();
        _listener.Ensure(ctx.AllCollections);
        return _listener.Last is { } last
            ? BatteryReading.Percentage(last.Percent, observedUtc: last.SeenUtc)
            : BatteryReading.NoData("waiting for the device to report");
    }

    // Check the signature, not just the length. Return null when unsure.
    internal static int? ParsePercent(byte[] r) =>
        r.Length >= 7 && r[0] == 0xBB && r[6] <= 100 ? r[6] : null;
}
```

Rules for `Read` and the parser:

- Unsure about the value? Return `NoData` with a short reason. Never guess,
  never return 0.
- On cable with no level: `BatteryReading.Charging()`.
- Dongle and cable of the same device should share one `LogicalKey`.
- If you ask: one question, wait for the answer, inside the reading, through
  `HidRequest`. No other file writes to a device.

## 4. Register it

Add it to `ProviderRegistry.CreateDefault()` in
`src/BatteryTray/Core/ProviderRegistry.cs`.

## 5. Test the parser

Add cases for your parser to `tools/diag/SelfTest.cs` (a good report, a short
one, a wrong signature, a value above 100) and run:

```bash
dotnet run --project tools/diag -c Release -- --selftest
```

## 6. Document it

Add the device to [supported-devices.md](supported-devices.md) and
[aparelhos-suportados.md](aparelhos-suportados.md). If you did not read it on
real hardware, it goes under "Not tested". If the format came from another
project, add it to `THIRD-PARTY-NOTICES.md`.

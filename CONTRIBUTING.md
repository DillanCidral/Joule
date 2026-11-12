# Contributing

[Português](CONTRIBUTING.pt-BR.md)

The most useful contribution is a device: either a capture from
**Send my device** (no code needed) or a provider you wrote
([docs/adding-a-device.md](docs/adding-a-device.md)).

## Build and test

You need Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
Run from the repository root.

```bash
dotnet build src/BatteryTray -c Debug
```

```bash
dotnet run --project tools/diag -c Release -- --selftest
```

```bash
dotnet run --project src/BatteryScan -c Release -- --selftest
```

Neither `--selftest` touches hardware. Run both after changing anything in
`Core`, `Interop`, `Providers` or `BatteryScan`.

To see the interface without hardware:

```bash
dotnet run --project src/BatteryTray -- --demo
```

## Before you open a pull request

- Read [SECURITY.md](SECURITY.md). A change that reads in a loop, opens a
  system mouse or keyboard collection, or writes to a device outside
  `Interop/HidRequest.cs` will not be merged.
- No hardware to test on? Say so. The device goes into the list marked
  "not tested".
- Taking something from another open project: bring only the protocol format
  and write the code yourself. For data (model tables), check the license
  first and name the source in the code and in `THIRD-PARTY-NOTICES.md`.
- Never commit captures, serial numbers, Bluetooth addresses, user names or
  PC names.
- Code comments are in Portuguese and the namespaces are still `BatteryTray`,
  the project's first name. English comments in new code are fine.

## License

By contributing you agree that your contribution is licensed under
[AGPL-3.0](LICENSE).

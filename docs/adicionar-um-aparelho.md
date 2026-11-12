# Como adicionar um aparelho

[English](adding-a-device.md)

Um aparelho é suportado por um **provider**: uma classe que sabe por quais IDs
USB ela responde e como transformar um relatório numa leitura de bateria. Nada
mais no app precisa saber que ela existe.

Leia antes o [SECURITY.pt-BR.md](../SECURITY.pt-BR.md). As regras dele não se
negociam.

## 1. Descubra como o aparelho informa a bateria

- **Já aparece no Windows** (*Configurações > Bluetooth*): não há o que fazer,
  o provider de Bluetooth já lê.
- **Um projeto aberto documenta** (Solaar, OpenRazer, HeadsetControl,
  OpenMouse, rivalcfg...): traga só o **formato**, escreva o código você mesmo
  e cite a origem num comentário. Antes de copiar dados, como tabelas de
  modelos, confira a licença.
- **Ninguém documenta**: capture. Rode o coletor enquanto usa o aparelho e
  procure um relatório cujo byte mude com a bateria:

```bash
dotnet run --project src/BatteryScan -c Release
```

A saída tem números de série e endereços Bluetooth. Não suba para o git.

## 2. Escolha o jeito mais leve que funcione

| O aparelho... | Use | Exemplo |
|---|---|---|
| manda a bateria sozinho, ou quando o programa do fabricante pergunta | `PassiveReportListener` (só escuta) | `RapooProvider`, `AttackSharkProvider` |
| manda relatórios o tempo todo | `HidOneShot.WaitForReport` (abre, lê um, fecha) | `DualSenseProvider` |
| só responde a uma pergunta | `HidRequest` (a única porta de escrita) | `LogitechProvider`, `SteelSeriesProvider` |
| não é uma coleção HID (slot XInput, propriedade do Windows) | `IStandaloneProvider` | `XInputProvider`, `BluetoothPnpProvider` |

Se o protocolo exige repetir a pergunta até duas respostas baterem, pare: ele
fica de fora.

## 3. Escreva o provider

Em `src/BatteryTray/Providers/`. Um provider que só escuta é deste tamanho:

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
            : BatteryReading.NoData("esperando o aparelho informar");
    }

    // Confira a assinatura, não só o tamanho. Na dúvida, devolva null.
    internal static int? ParsePercent(byte[] r) =>
        r.Length >= 7 && r[0] == 0xBB && r[6] <= 100 ? r[6] : null;
}
```

Regras para o `Read` e para o parser:

- Dúvida sobre o valor? Devolva `NoData` com um motivo curto. Nunca chute,
  nunca devolva 0.
- No cabo e sem nível: `BatteryReading.Charging()`.
- Dongle e cabo do mesmo aparelho devem ter a mesma `LogicalKey`.
- Se for perguntar: uma pergunta, espera a resposta, dentro da leitura, pelo
  `HidRequest`. Nenhum outro arquivo escreve no aparelho.

## 4. Registre

Acrescente em `ProviderRegistry.CreateDefault()`, no arquivo
`src/BatteryTray/Core/ProviderRegistry.cs`.

## 5. Teste o parser

Acrescente casos do seu parser em `tools/diag/SelfTest.cs` (um relatório bom,
um curto, uma assinatura errada, um valor acima de 100) e rode:

```bash
dotnet run --project tools/diag -c Release -- --selftest
```

## 6. Documente

Ponha o aparelho em [aparelhos-suportados.md](aparelhos-suportados.md) e em
[supported-devices.md](supported-devices.md). Se você não leu num aparelho de
verdade, ele entra em "Não testados". Se o formato veio de outro projeto,
acrescente no `THIRD-PARTY-NOTICES.md`.

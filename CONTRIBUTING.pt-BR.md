# Como contribuir

[English](CONTRIBUTING.md)

A contribuição mais útil é um aparelho: uma captura do **Mandar meu aparelho**
(não precisa de código) ou um provider escrito por você
([docs/adicionar-um-aparelho.md](docs/adicionar-um-aparelho.md)).

## Compilar e testar

Você precisa de Windows e do [SDK do .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).
Rode da raiz do repositório.

```bash
dotnet build src/BatteryTray -c Debug
```

```bash
dotnet run --project tools/diag -c Release -- --selftest
```

```bash
dotnet run --project src/BatteryScan -c Release -- --selftest
```

Nenhum dos dois `--selftest` toca em hardware. Rode os dois depois de mexer em
`Core`, `Interop`, `Providers` ou `BatteryScan`.

Para ver a interface sem hardware:

```bash
dotnet run --project src/BatteryTray -- --demo
```

## Antes de abrir um pull request

- Leia o [SECURITY.pt-BR.md](SECURITY.pt-BR.md). Mudança que lê em loop, abre
  coleção de mouse ou teclado de sistema, ou escreve no aparelho fora do
  `Interop/HidRequest.cs` não entra.
- Não tem o aparelho para testar? Diga. Ele entra na lista marcado como
  "não testado".
- Vai trazer algo de outro projeto aberto: traga só o formato do protocolo e
  escreva o código você mesmo. Para dados (tabelas de modelos), confira a
  licença antes e cite a origem no código e no `THIRD-PARTY-NOTICES.md`.
- Nunca suba capturas, números de série, endereços Bluetooth, nomes de usuário
  ou nomes de PC.
- Os comentários do código estão em português e os namespaces ainda são
  `BatteryTray`, o primeiro nome do projeto. Comentário em inglês em código
  novo é bem-vindo.

## Licença

Ao contribuir, você concorda que a sua contribuição fica sob a
[AGPL-3.0](LICENSE).

# Arquitetura

[English](architecture.md)

O Joule é um executável único e autocontido, `win-x64` (.NET 8, WPF), que roda
sem direitos de administrador.

## Camadas

```
src/BatteryTray/
  Core/       modelo de leitura, registro de providers, cache, configurações, log
  Interop/    APIs do Windows: HID, SetupAPI (Bluetooth), XInput
  Providers/  uma classe por família de hardware
  Ui/         painel, widget de borda, ícone da bandeja, boas-vindas, "Mandar meu aparelho"
src/BatteryScan/   JouleScan: a ferramenta de captura; o app reaproveita o código dela
tools/diag/        diagnóstico e o autoteste
```

`Core`, `Interop` e `Providers` não dependem da interface. O mesmo código de
leitura roda no app, no JouleScan e no `tools/diag`.

Os namespaces e as pastas ainda dizem `BatteryTray`, o primeiro nome do projeto.

## Como acontece uma leitura

1. O `HidInventory.Scan` lista as coleções HID presentes. A varredura pede
   strings aos aparelhos, então só roda dentro de uma leitura sob demanda.
2. O `ProviderRegistry` entrega cada VID:PID conhecido ao provider dele. Os
   providers autônomos (XInput, Bluetooth, as marcas que perguntam) descobrem
   os próprios aparelhos na mesma lista.
3. Cada provider devolve um `BatteryReading`: percentual, faixa, "carregando"
   ou "sem dado" com o motivo.
4. Aparelho sem provider aparece como "sem suporte". Nunca some e nunca mostra
   valor.
5. O último valor de cada aparelho fica em cache no disco e aparece com a
   idade quando uma leitura volta vazia.

## Sob demanda, mais duas fontes ao vivo

Uma leitura tem quatro gatilhos: abrir o app, login do Windows, Atualizar e o
Windows avisar que um aparelho conectou. Esse último espera 1,5 s para o
aparelho assentar, lê só o que entrou e roda no máximo uma vez a cada 10 s. O
alerta de bateria baixa é avaliado no fim de uma leitura, não por um vigia.

Fica ao vivo só o que não toca no aparelho:

- **Escuta passiva** (Attack Shark, Rapoo): um handle HID, aberto uma vez,
  recebe os relatórios que já estão passando. Escuta que caiu não é reaberta
  fora de uma leitura sob demanda.
- **Bluetooth**: a cada 60 segundos o Joule relê o percentual que o Windows já
  guardou no nó do aparelho. Nada vai pelo rádio.

A leitura ao vivo reaproveita as coleções da última varredura completa.

## A porta única de escrita

Os providers que precisam perguntar a bateria fazem isso pelo
`Interop/HidRequest.cs`, o único lugar do projeto que escreve num aparelho.
Ele recusa coleções de mouse e teclado de sistema, manda uma pergunta por vez,
espera a resposta e limita a sessão a 48 perguntas. Veja o
[SECURITY.pt-BR.md](../SECURITY.pt-BR.md).

## Dados, instalação e atualização

- Os dados ficam em `%LOCALAPPDATA%\Joule\`: `settings.json`, `state.json`
  (cache) e `joule.log`.
- Instalador e atualizador são do [Velopack](https://github.com/velopack/velopack).
  O app consulta o GitHub Releases ao abrir e a cada hora.
- Uma segunda instância não abre: ela pede à que já está rodando para mostrar
  o painel.

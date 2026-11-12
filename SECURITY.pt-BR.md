# Segurança e privacidade

[English](SECURITY.md)

O Joule conversa com aparelhos dos quais você depende. Durante o
desenvolvimento, uma leitura HID em loop derrubou um mouse e um fone do USB
por horas. As regras abaixo existem por isso, e o código as impõe.

## Regras que o código impõe

| Regra | Onde |
|---|---|
| Ler só sob demanda: abrir o app, login do Windows, botão Atualizar e o Windows avisar que um aparelho conectou (uma leitura só desse aparelho, no máximo uma a cada 10 s). Sem loop, sem timer | `Core/ReadingService.cs` |
| Um handle aberto uma vez, leitura contínua, **um único** `CancelIoEx` no fim | `Interop/HidPassiveReader.cs` |
| Nunca abrir as coleções de mouse (`0x01`/`0x02`) ou teclado (`0x01`/`0x06`) de sistema | `HidInventory.IsSystemReserved` |
| Nunca leitura bloqueante em coleção sem relatório de entrada | `HidPassiveReader.Open` |
| Aparelho sumiu no meio da operação: parar de tocar nele | laço do `HidPassiveReader` |
| Escrita só por uma porta: só a pergunta de bateria que projetos abertos já usam, uma por vez, esperando a resposta, no máximo 48 por leitura somando todos os aparelhos | `Interop/HidRequest.cs` (o único `WriteFile` do projeto) |
| Marca ainda não conferida em aparelho real não recebe pergunta até você permitir (um aviso quando o aparelho aparece, ou **Permitir** no painel). A permissão vale por marca e sai nas Configurações | `HidRequest` (a mesma porta) |
| **Só escutar**, nas Configurações, desliga toda pergunta, até nas marcas permitidas | `HidRequest` |
| Modelo relatado com problema entra numa lista de bloqueio e nunca recebe pergunta, mesmo permitido | `HidRequest.Blocked` |
| Nenhum código de internet no app e no JouleScan fora do atualizador, que só aceita este repositório | conferido pelo `tools/diag --selftest` |
| A varredura HID pede strings ao aparelho, então nunca roda em timer | `Interop/HidInventory.cs` |
| Nunca inventar número. Sem dado é "sem dado", nunca "0%" | todos os providers |

Entre uma leitura e outra, fica ao vivo só o que não toca no aparelho: a
escuta passiva que já está aberta e o percentual do Bluetooth que o próprio
Windows guardou. Passar o mouse no widget nunca dispara leitura.

## O que o Joule manda para o aparelho

- **Só escuta**: Attack Shark, Rapoo, Incott, DualSense, controles XInput e
  Bluetooth (no Bluetooth ele lê o valor que o Windows guardou; nada vai pelo
  rádio).
- **Pergunta a bateria**: Logitech, Razer, SteelSeries, HyperX, Corsair, o
  protocolo Compx (ATK, VXE, VGN, Pulsar, Dareu) e parte das outras marcas.
  Só a pergunta de leitura, nunca comando que mude configuração.
- Protocolos que exigem repetir a pergunta até duas respostas baterem ficam de
  fora de propósito.

## Privacidade

- Sem telemetria, sem conta, sem servidor. O único acesso à internet é o
  atualizador consultando o GitHub Releases.
- O Joule grava só em `%LOCALAPPDATA%\Joule\`: `settings.json`, `state.json`
  (último valor de cada aparelho) e `joule.log`.
- O **Mandar meu aparelho** salva um `.txt` em `%LOCALAPPDATA%\Joule\envios\`
  depois de tirar números de série, endereços Bluetooth, o nome do PC, o nome
  de usuário e os Bluetooth que não são periférico (celular, caixa de som,
  carro). Você vê o arquivo antes de mandar, e quem manda é você. Um relato no
  GitHub é público.

## Se um aparelho se comportar mal

Se um aparelho travou, sumiu do USB ou ficou estranho depois de abrir o Joule:

1. Feche o Joule (ícone da bandeja > Sair) e replugue o dongle.
2. Conte para nós: abra um [relato](https://github.com/DillanCidral/Joule/issues)
   ou escreva para dillan.apps@gmail.com com o modelo do aparelho, o
   `joule.log` (ícone da bandeja > pasta de dados) e qual software do
   fabricante estava aberto.

Se a causa for um provider que pergunta, aquele aparelho é desligado numa
versão nova primeiro e investigado depois.

## Relatar uma vulnerabilidade

Escreva para dillan.apps@gmail.com em vez de abrir um relato público.

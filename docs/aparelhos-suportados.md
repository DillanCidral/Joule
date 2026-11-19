# Aparelhos suportados

[English](supported-devices.md)

**Conferido** quer dizer lido num aparelho de verdade. **Não testado** quer
dizer escrito a partir de documentação aberta de protocolo, sem ninguém ter
confirmado no aparelho ainda. Se você tem um "não testado", conte se funciona:
abra um [relato](https://github.com/DillanCidral/Joule/issues) ou use o
**Mandar meu aparelho** dentro do Joule.

"Escuta" quer dizer que o Joule não manda nada para o aparelho. "Pergunta"
quer dizer que cada consulta do protocolo é enviada uma vez por leitura (veja o
[SECURITY.pt-BR.md](../SECURITY.pt-BR.md)).

## Conferidos

| Aparelho | Conexão | Como | Observações |
|---|---|---|---|
| Attack Shark R1 | dongle | escuta | Atualiza sozinho enquanto o mouse está acordado. Mouse parado não manda nada |
| Attack Shark R1 | cabo | presença | Mostra "carregando", sem percentual |
| Attack Shark V6 | dongle | escuta | Visto só em 100% até agora (o mouse manda uma mensagem de "cheio"); níveis menores seguem o formato do X11, mas não foram testados |
| Rapoo MT760 Mini | dongle | escuta | O mouse só informa com o programa da Rapoo aberto; sem ele, o Joule mostra o último valor com a idade |
| Razer BlackShark V3 | dongle | pergunta | Pergunta uma vez por leitura (conferido no aparelho); entre leituras guarda o que o fone informou sozinho, com a idade |
| DualSense (PS5) | cabo; Bluetooth ainda não testado | escuta | Por Bluetooth o controle só informa a bateria depois que um programa como a Steam escreve nele; o Joule só lê o que ele já manda |
| DualShock 4 (PS4) | cabo e Bluetooth, ainda não testado | escuta | Igual ao DualSense no Bluetooth |
| Controles Xbox | adaptador, cabo | XInput | Faixa (baixa / média / cheia) pelo adaptador; "no cabo" quando plugado |
| 8BitDo Ultimate 2C | cabo | XInput | Só "no cabo", sem percentual. Pelo dongle ele informa um valor falso, que o Joule descarta |
| Qualquer aparelho Bluetooth | Bluetooth | lê o Windows | O mesmo número de *Configurações > Bluetooth*. Nada vai pelo rádio |
| Zaopin ZPW MAX (8K) | receptor `3554:F527` | pergunta | Percentual confirmado no aparelho real |
| AULA F75 | cabo `258A:010C` | presença | Mostra "no cabo", sem inventar um percentual |
| AJAZZ 2.4G 8K | receptor GearHub-V5 `3151:402D` | pergunta | Estado `F7` do receptor; percentual confirmado no aparelho real |
| ATK A9 Ultra V2 | receptor 8K NANO `373B:11FE` | pergunta | Percentual confirmado no aparelho real. Mouse dormindo não responde; mexa nele e a próxima leitura pega |
| ATK 1K DONGLE-PAW (`373B:10F0`) | dongle | pergunta | Uma consulta de bateria por leitura, independente do ATK HUB. O receptor pode indicar carregamento mesmo sem cabo |
| ATK Mercury I / M1 (`373B:107C`) | dongle | pergunta | Receptor `0005`, fone `0007`. Três consultas conferem versão, estrutura e percentual. Estado de carregamento pendente |
| ASUS ROG Pelta | receptor 2.4 GHz `0B05:1B84` | pergunta, depois escuta | Pergunta nível e carregamento uma vez por leitura; entre leituras escuta o nível que o fone manda sozinho (ao vivo). O carregamento entre leituras segue o nível subindo ou descendo. Confirmado no aparelho real. Feche o Armoury Crate, que usa o mesmo canal |

### Leitura dos aparelhos ATK conferidos

As consultas seguem as permissões de marca, o modo **Só escutar** e o limite
global de perguntas do Joule. Autorize a marca ATK quando o aplicativo solicitar.
Os providers são acionados na abertura do Joule, em **Atualizar** e na chegada
USB do receptor. A leitura de um receptor já conectado depende de **Atualizar**.

- O mouse `373B:10F0` usa a coleção `FF10/1`, entrada de 65 bytes e feature
  de 9 bytes. Envia `00 82 00 00 00 00 00 00 00` uma vez e aguarda até três
  segundos pela resposta interrupt `82 08 02 00 FF 03`. No relatório do Windows,
  os sete bits inferiores do byte 9 trazem o percentual e o bit superior indica
  carregamento. O parser confere a assinatura e a faixa de 0 a 100. A consulta
  foi validada com o ATK HUB fechado, inclusive com leitura de 27%. O bit de
  carregamento pode vir ativo mesmo com o mouse fora do cabo.
- O Mercury `373B:107C` usa a coleção `FF22/1` em `mi_00/col02`, com features
  de 64 bytes. O receptor precisa ser `0005`. O comando `AA 01` confirma o
  firmware `0007` do fone. Duas consultas `AA 03` leem quatro bytes cada,
  primeiro o ponteiro em `0x0100FE94`, que deve conter `0x0100FE5C`, depois
  a capacidade em `0x0100FE78`. Os endereços são enviados em big-endian e os
  valores retornam em little-endian. Cada consulta espera 300 ms antes de ler
  uma resposta. Versão, ponteiro ou percentual inválido produzem **sem dado**.
  Nas comparações com o Bluetooth em 100%, 90% e 80%, o canal retornou 100%,
  93% e 89%, respectivamente. Os testes também conferiram ausência de resposta
  com o fone desligado. O estado de carregamento ainda precisa ser mapeado.

## Confirmados por usuários

Escritos a partir de documentação aberta e confirmados por alguém que mandou
uma captura mostrando o Joule lendo no próprio PC (novembro de 2026).

| Aparelho | Conexão |
|---|---|
| Mouses Logitech no LIGHTSPEED (PRO X 2 e outros) | receptor LIGHTSPEED |
| Logitech MX Master 2S | receptor Unifying |
| SteelSeries Arctis 1 Wireless e Arctis 7 | dongle |
| VXE R1 Pro Max | receptor `3554:F58A` |
| VGN IO Aurora e Aurora Pro 8K | receptores `3554:FB08` e `3554:FB40` |
| AULA AU75 | receptor `3554:FA09` (o mesmo do F75) |
| Razer Basilisk V3 Pro 35K | dongle `1532:00CD` |
| SteelSeries Arctis Nova 7 Gen 2 | dongle `1038:227E` |
| Lamzu Maya X | dongle 8K `373E:001E` |
| Dark Project Novus | receptor VGN/VXE `3554:FB0A` |
| HyperX Cloud Alpha 2 Wireless | dongle |
| AULA F75 | receptor `3554:FA09` |
| Pulsar TenZ Signature Edition | receptor Pulsar 8K |
| SteelSeries Arctis Nova 3P / 3X | dongle |
| Logitech G733 | dongle |

## Não testados

| Marca | Aparelhos | Como |
|---|---|---|
| Logitech | Aparelhos nos receptores Unifying, Bolt, Lightspeed e Nano; fones G533, G535, G633, G635, G733, G933, G935, G PRO, PRO X | pergunta (HID++ 2.0) |
| Razer | 71 mouses pelo dongle e no cabo, 13 teclados sem fio, fone Nari | pergunta (relatório de feature) |
| SteelSeries | Arctis 1, 7, 7+, Nova 3P / 5 / 7 / Pro, GameBuds; Aerox 3 / 5 / 9, Prime, Rival 3, Rival 650 | pergunta |
| HyperX | Cloud II Wireless, Cloud Alpha Wireless, Cloud Alpha 2 Wireless, Cloud Flight; Pulsefire Dart | pergunta |
| Corsair | 22 mouses e teclados pelo dongle Slipstream; Void, Void Wireless V2, HS80 Max, Virtuoso Max, Virtuoso XT / SE | pergunta (só leitura) |
| Attack Shark | X6, X11, X11 SE, Delux M600 Pro no mesmo receptor do R1 | escuta |
| Attack Shark | R2 (receptor GearHub-V5), R5 Ultra, R6, R8 | pergunta |
| ATK, VXE, VGN, Pulsar, Dareu, Hitscan | protocolo do "relatório 8" dos chips Compx/Beken (192 modelos ATK) | pergunta |
| AULA | F75 (receptor `3554:FA09`) | pergunta só a bateria pelo receptor |
| G-Wolves | gerações xvi e HTX Ultra | pergunta |
| Lamzu, LunaFury, CRDRAKO, Vancer, RAWM, WLmouse | mesma família de protocolo | pergunta |
| AJAZZ | NJ07, NJ08 | pergunta |
| K-Snake / Noir | X11, M2-NEX | pergunta |
| Rapoo | VT9 Pro | lê uma feature, sem pergunta |
| Lingbao | M5 Pro (receptor GearHub-V5; também o teclado pareado) | pergunta |
| Bytech, Motospeed X6, mouses Keychron | | pergunta |
| Incott | receptor e cabo | escuta |

## O que falta

- **Logitech**: aparelhos HID++ 1.0, os fones "Centurion" (G PRO X 2
  Lightspeed, G522, Astro A50), Logitech no cabo.
- **Razer**: os outros fones.
- **SteelSeries**: Arctis 9, Arctis Pro Wireless.
- **HyperX**: outros Pulsefire, teclados sem fio.
- **Corsair**: dongle Slipstream V2, Katar Pro Wireless, Sabre V2 Pro,
  aparelhos no cabo.
- **MCHOSE, Ninjutso, o resto do Rapoo VT9**: o protocolo exige repetir a
  pergunta até as respostas baterem. Ficam de fora de propósito.
- Mouses **Keychron** sem a coleção `0xFFC1`.
- Um aparelho no dongle **e** no Bluetooth aparece em duas linhas.
- Dois DualSense aparecem numa linha só.

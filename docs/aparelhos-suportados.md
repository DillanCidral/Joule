# Aparelhos suportados

[English](supported-devices.md)

**Conferido** quer dizer lido num aparelho de verdade. **Não testado** quer
dizer escrito a partir de documentação aberta de protocolo, sem ninguém ter
confirmado no aparelho ainda. Se você tem um "não testado", conte se funciona:
abra um [relato](https://github.com/DillanCidral/Joule/issues) ou use o
**Mandar meu aparelho** dentro do Joule.

"Escuta" quer dizer que o Joule não manda nada para o aparelho. "Pergunta"
quer dizer que ele manda a pergunta de bateria uma vez por leitura (veja o
[SECURITY.pt-BR.md](../SECURITY.pt-BR.md)).

## Conferidos

| Aparelho | Conexão | Como | Observações |
|---|---|---|---|
| Attack Shark R1 | dongle | escuta | Atualiza sozinho enquanto o mouse está acordado. Mouse parado não manda nada |
| Attack Shark R1 | cabo | presença | Mostra "carregando", sem percentual |
| Rapoo MT760 Mini | dongle | escuta | O mouse só informa com o programa da Rapoo aberto; sem ele, o Joule mostra o último valor com a idade |
| DualSense (PS5) | só no cabo | escuta | Por Bluetooth o controle só informa a bateria depois que o PC escreve nele, então esse caminho fica de fora |
| Controles Xbox | adaptador, cabo | XInput | Faixa (baixa / média / cheia) pelo adaptador; "no cabo" quando plugado |
| 8BitDo Ultimate 2C | cabo | XInput | Só "no cabo", sem percentual. Pelo dongle ele informa um valor falso, que o Joule descarta |
| Qualquer aparelho Bluetooth | Bluetooth | lê o Windows | O mesmo número de *Configurações > Bluetooth*. Nada vai pelo rádio |

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

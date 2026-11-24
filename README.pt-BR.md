<h1 align="center"><img src="docs/images/capa.png" alt="Joule" width="100%"></h1>

<p align="center"><strong>Bateria do mouse, fone, controle e teclado sem fio, na bandeja do Windows.</strong></p>

<p align="center">
  <a href="https://github.com/DillanCidral/Joule/releases/latest"><img src="https://img.shields.io/github/v/release/DillanCidral/Joule?label=vers%C3%A3o&color=FFC21F&labelColor=1D1B18" alt="Versão mais nova"></a>
  <a href="https://github.com/DillanCidral/Joule/releases"><img src="https://img.shields.io/github/downloads/DillanCidral/Joule/total?label=downloads&color=FFC21F&labelColor=1D1B18" alt="Downloads"></a>
  <img src="https://img.shields.io/badge/Windows-11-FFC21F?labelColor=1D1B18" alt="Windows 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/licen%C3%A7a-AGPL--3.0-FFC21F?labelColor=1D1B18" alt="Licença: AGPL-3.0"></a>
</p>

<p align="center"><a href="README.md"><img src="docs/images/bandeira-us.png" alt="" height="14"> English</a> · <img src="docs/images/bandeira-br.png" alt="" height="14"> <strong>Português</strong> · <a href="README.fr.md"><img src="docs/images/bandeira-fr.png" alt="" height="14"> Français</a> · <a href="README.ru.md"><img src="docs/images/bandeira-ru.png" alt="" height="14"> Русский</a> · <a href="README.zh-TW.md"><img src="docs/images/bandeira-tw.png" alt="" height="14"> 繁體中文</a></p>

> [!WARNING]
> **Windows Defender e a versão 0.2.3.** Desde 20/11/2026, o Defender marca o
> instalador como `Trojan:Win32/Yomal!rfn` e o coloca em quarentena. Os arquivos
> da versão são os mesmos publicados em 19/11, e o app montado de novo a partir
> deste código bate com o que está na versão. Tudo indica um alarme falso, e o
> arquivo já foi enviado à Microsoft para análise. Se o Defender pegou o Joule,
> pode deixar em quarentena e esperar: este aviso muda quando a Microsoft
> responder. Detalhes na [página da 0.2.3](https://github.com/DillanCidral/Joule/releases/tag/v0.2.3).

<p align="center">
  <a href="https://github.com/DillanCidral/Joule/releases/latest/download/JouleApp-win-Setup.exe"><img src="https://img.shields.io/badge/Baixar_para_Windows-FFC21F?style=for-the-badge" alt="Baixar para Windows" height="40"></a>
</p>

<p align="center"><img src="docs/images/joule.png" alt="Painel e widget de borda do Joule, tema escuro" width="85%"></p>

O Joule é um app pequeno para Windows 11 que fica na bandeja e mostra quanta
bateria ainda têm os seus aparelhos sem fio. É gratuito, de código aberto e
está em **beta**.

## O que ele tem

- **Painel**: clique no ícone da bandeja para ver cada aparelho conectado, do
  mais fraco ao mais forte.
- **Widget de borda**: anéis de bateria na borda da tela. Esconde sozinho e
  some quando um app está em tela cheia.
- **Ícone da bandeja**: mostra de relance se algo está com bateria baixa;
  passe o mouse para ver a bateria de cada aparelho.
- **Aviso ao conectar**: quando um aparelho liga, o Joule mostra a bateria dele,
  no widget ou no aviso do Windows, com som se você quiser.
- **Alerta de bateria baixa**, com o limite que você escolher.
- **Português, inglês, francês e russo**: segue o idioma do Windows, e dá para trocar
  nas Configurações. O francês e o russo foram traduzidos por quem usa o Joule.
- **Verde quando completa**: aparelho que informa a carga fica verde no widget e
  na bandeja ao chegar a 100%.
- **Resumo depois de atualizar**: quantos aparelhos o Joule acompanhou e quantos
  avisos de bateria baixa deu, contados só no seu PC, e o que mudou na versão.

## Baixar

Baixe o `JouleApp-win-Setup.exe` pelo botão acima ou na
[página de Releases](https://github.com/DillanCidral/Joule/releases), e abra.

- Não precisa de administrador. Instala só para o seu usuário e aparece em
  *Aplicativos instalados*, de onde se desinstala.
- O instalador não tem assinatura digital, então o SmartScreen do Windows vai
  avisar. Clique em **Mais informações** e depois em **Executar assim mesmo**.
- O Joule se atualiza sozinho pelo GitHub Releases e avisa na bandeja quando
  há versão nova.

## Aparelhos suportados

| Aparelho | Conexão | Situação |
|---|---|---|
| Attack Shark R1 | dongle | ✅ conferido |
| Rapoo MT760 Mini | dongle | ✅ conferido |
| ATK 1K DONGLE-PAW | dongle | ✅ conferido |
| ATK Mercury I / M1, receptor `0005` e fone `0007` | dongle | ✅ conferido |
| Razer BlackShark V3 | dongle | ✅ conferido |
| Zaopin ZPW MAX (8K) | dongle | ✅ conferido |
| Receptor AJAZZ 2.4G 8K | dongle | ✅ conferido |
| AULA F75 | cabo | ✅ conferido |
| DualSense | cabo | ✅ conferido |
| DualSense no Bluetooth, DualShock 4 | cabo, Bluetooth (com a Steam ou programa parecido aberto) | 🧪 ainda não testado |
| Controles Xbox | adaptador e cabo | ✅ conferido |
| 8BitDo Ultimate 2C | cabo | ✅ conferido |
| Switch Pro Controller | Bluetooth (com a Steam ou programa parecido aberto) | ✅ conferido |
| Qualquer aparelho Bluetooth cuja bateria o Windows já mostre em *Configurações > Bluetooth* | Bluetooth | ✅ conferido |
| Mouses Logitech no LIGHTSPEED (PRO X 2 e outros) e no Unifying (MX Master 2S), SteelSeries Arctis 1 e Arctis 7, VXE R1 Pro Max, VGN IO Aurora, AULA AU75, Razer Basilisk V3 Pro 35K, SteelSeries Arctis Nova 7 Gen 2, Lamzu Maya X, Dark Project Novus, HyperX Cloud Alpha 2 Wireless, AULA F75 (dongle), Pulsar TenZ, SteelSeries Arctis Nova 3P/3X, Logitech G733 | dongle | ✅ confirmado por usuários |
| Logitech, Razer, SteelSeries, HyperX e Corsair: mouses, teclados e fones | dongle | 🧪 ainda não testado |
| Umas 20 outras marcas: ATK, VXE, VGN, Pulsar, Dareu, AJAZZ, Lamzu, WLmouse, G-Wolves, Hitscan, K-Snake, Bytech, Motospeed, Keychron, Incott e outros modelos Attack Shark | dongle | 🧪 ainda não testado |

🧪 quer dizer escrito a partir de documentação aberta de protocolo e ainda não
testado em aparelho real. Se você tem um, é o seu relato que confirma.
"Confirmado por usuários" quer dizer que alguém mandou uma captura mostrando o
Joule lendo no PC dela. Lista
completa: [docs/aparelhos-suportados.md](docs/aparelhos-suportados.md).

## Meu aparelho não está na lista

Antes, veja as [famílias de aparelhos](https://github.com/DillanCidral/Joule/issues?q=is%3Aissue%20is%3Aopen%20label%3Atracking): se o seu estiver lá, dê um 👍
em vez de abrir outro relato. O Joule também mostra pedidos parecidos na hora
de enviar.

Abra **Configurações > Avançado > Enviar um aparelho** dentro do Joule. Ele escuta por
30 segundos enquanto você usa o aparelho, mostra o que achou, tira os dados
pessoais (número de série, endereço Bluetooth, nome do PC, nome de usuário) e
salva um `.txt`. Quem manda o arquivo é você, num
[relato no GitHub](https://github.com/DillanCidral/Joule/issues/new/choose) ou
por [e-mail](#contato). Nada sai do seu PC sozinho.

Quer adicionar o aparelho você mesmo? Veja
[docs/adicionar-um-aparelho.md](docs/adicionar-um-aparelho.md).

<details>
<summary><strong>O que o Joule faz com os seus aparelhos</strong></summary>

- Lê quando você abre o app, quando você entra no Windows, quando você clica em
  **Atualizar**, quando você liga ou conecta um aparelho (só esse aparelho, no
  máximo uma vez a cada 10 segundos) e quando você abre o painel com a última
  leitura tendo mais de uma hora. Nunca fica perguntando em loop.
- Na maioria dos aparelhos ele só **escuta** o que o dongle ou o cabo já
  mandam, ou lê o número que o próprio Windows guardou (Bluetooth).
- Para algumas marcas pelo dongle (Logitech, Razer, SteelSeries, HyperX,
  Corsair e parte das outras) ele **pergunta** a bateria ao aparelho, só
  depois que você permite aquela marca: a mesma
  pergunta que projetos abertos como Solaar, OpenRazer e HeadsetControl mandam,
  uma vez por leitura. Nunca manda comando que mude configuração. A chave
  **Só escutar**, nas Configurações, corta toda pergunta.
- Nunca mostra número que não leu. Sem dado é "sem dado", nunca "0%".
- Não manda nada para lugar nenhum. O único acesso à internet é a busca de
  atualização no GitHub Releases.

Detalhes: [SECURITY.pt-BR.md](SECURITY.pt-BR.md).

</details>

<details>
<summary><strong>Compilar</strong></summary>

Você precisa de Windows e do [SDK do .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build src/BatteryTray -c Debug
```

```bash
dotnet run --project src/BatteryTray -- --demo
```

O `--demo` mostra aparelhos de exemplo sem tocar em hardware nenhum. Mais em
[CONTRIBUTING.pt-BR.md](CONTRIBUTING.pt-BR.md) e
[docs/arquitetura.md](docs/arquitetura.md).

</details>

## Obrigado

A versão 0.2.3 traz código e tradução de [@DiogoLss](https://github.com/DiogoLss) (ATK 1K DONGLE-PAW e
Mercury I / M1), [@BatuhanUyurca](https://github.com/BatuhanUyurca) (ATK A9 Ultra V2), [@Ali-Fani](https://github.com/Ali-Fani) (Attack Shark V6),
[@mochi0820](https://github.com/mochi0820) (chinês tradicional), [@TukaLeHibou](https://github.com/TukaLeHibou) (francês revisado) e [@wrldwideme](https://github.com/wrldwideme)
(README em russo), e o formato da hora veio de uma ideia do Yim.

A versão 0.2.1 tem código e tradução da comunidade:
[@Ali-Fani](https://github.com/Ali-Fani) (Razer BlackShark V3),
[@wrldwideme](https://github.com/wrldwideme) (AULA F75, receptores AJAZZ e
Zaopin e a tradução para o russo),
[@TukaLeHibou](https://github.com/TukaLeHibou) (tradução para o francês), e o
verde da bateria completa veio de uma ideia do Daniel Palma. Obrigado, e
obrigado a todos que mandaram captura.

## Apoiar o Joule

O Joule é grátis e de código aberto. Se ele te ajuda, um café ajuda a mantê-lo
assim: [apoiar o Joule](https://buy.stripe.com/bJeeVe5yq5Pub7bfz8bZe00).

## Contato

- E-mail: dillan.apps@gmail.com
- Instagram: [@dillan.apps](https://www.instagram.com/dillan.apps/)
- YouTube: [@dillanapps](https://www.youtube.com/@dillanapps)

## Licença

[AGPL-3.0](LICENSE). As licenças de terceiros e os projetos abertos cuja
documentação de protocolo tornou o Joule possível estão em
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

O Joule não tem ligação com Logitech, Razer, SteelSeries, HyperX, Corsair,
Sony, Microsoft, Nintendo, 8BitDo nem com outra marca citada aqui. Os nomes e as marcas
pertencem aos seus donos.

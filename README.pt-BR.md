# Joule

**Bateria do mouse, fone, controle e teclado sem fio, na bandeja do Windows.**

[![Versão mais nova](https://img.shields.io/github/v/release/DillanCidral/Joule)](https://github.com/DillanCidral/Joule/releases/latest)
[![Licença: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue)](LICENSE)
![Windows 11](https://img.shields.io/badge/Windows-11-0078D4)

[English](README.md)

![Painel e widget de borda do Joule, tema escuro](docs/images/joule.png)

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
- **Português e inglês**: segue o idioma do Windows, e dá para trocar nas
  Configurações.
- **Resumo depois de atualizar**: quantos aparelhos o Joule acompanhou e quantos
  avisos de bateria baixa deu, contados só no seu PC, e o que mudou na versão.

## Baixar

Baixe o `JouleApp-win-Setup.exe` na
[página de Releases](https://github.com/DillanCidral/Joule/releases) e abra.

- Não precisa de administrador. Instala só para o seu usuário e aparece em
  *Aplicativos instalados*, de onde se desinstala.
- O instalador não tem assinatura digital, então o SmartScreen do Windows vai
  avisar. Clique em **Mais informações** e depois em **Executar assim mesmo**.
- O Joule se atualiza sozinho pelo GitHub Releases e avisa na bandeja quando
  há versão nova.

## Aparelhos suportados

Conferidos em aparelho real:

- **Attack Shark R1** (dongle)
- **Rapoo MT760 Mini** (dongle)
- **DualSense** (cabo), **controles Xbox** (adaptador e cabo), **8BitDo Ultimate 2C** (cabo)
- **Qualquer aparelho Bluetooth** cuja bateria o Windows já mostre em *Configurações > Bluetooth*

Em beta, escritos a partir de documentação aberta de protocolo e **ainda não
testados em aparelho real**. Se você tem um, é o seu relato que confirma:

- **Logitech, Razer, SteelSeries, HyperX e Corsair**: mouses, teclados e fones
- **Umas 20 outras marcas**: ATK, VXE, VGN, Pulsar, Dareu, AJAZZ, Lamzu, WLmouse,
  G-Wolves, Hitscan, K-Snake, Bytech, Motospeed, Keychron, Incott e outros
  modelos Attack Shark

Lista completa: [docs/aparelhos-suportados.md](docs/aparelhos-suportados.md).

## O que o Joule faz com os seus aparelhos

- Lê quando você abre o app, quando você entra no Windows, quando você clica em
  **Atualizar** e quando você liga ou conecta um aparelho (só esse aparelho, no
  máximo uma vez a cada 10 segundos). Nunca fica perguntando em loop.
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

## Meu aparelho não está na lista

Abra **Configurações > Mandar meu aparelho** dentro do Joule. Ele escuta por
30 segundos enquanto você usa o aparelho, mostra o que achou, tira os dados
pessoais (número de série, endereço Bluetooth, nome do PC, nome de usuário) e
salva um `.txt`. Quem manda o arquivo é você, num
[relato no GitHub](https://github.com/DillanCidral/Joule/issues/new/choose) ou
por e-mail. Nada sai do seu PC sozinho.

Quer adicionar o aparelho você mesmo? Veja
[docs/adicionar-um-aparelho.md](docs/adicionar-um-aparelho.md).

## Compilar

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
Sony, Microsoft, 8BitDo nem com outra marca citada aqui. Os nomes e as marcas
pertencem aos seus donos.

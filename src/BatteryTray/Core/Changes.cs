namespace BatteryTray.Core;

/// <summary>Grupos da tela "O que mudou" (2026-11-17, escolhidos pelo Dillan), nesta ordem.</summary>
public enum ChangeGroup { Devices, Confirmed, Languages, Fixes }

/// <summary>Uma novidade da tela "O que mudou" (2026-11-12). Glyph = traco 20x20, como os desenhos das telas.</summary>
/// Credit = agradecimento a quem fez (2026-11-16): "@login" do PR; nas linguas, na lingua de quem traduziu, igual em qualquer idioma do app.
/// Text: em Aparelhos e Confirmados, o tipo (Mouse, Fone) embaixo do nome; em Idiomas nao aparece; em Ajustes, a frase.
/// Important = aparece tambem para quem ja estava nesta versao, por mais uma atualizacao (2026-11-17).
public sealed record Change(string Version, ChangeGroup Group, string Glyph, string TitlePt, string TitleEn, string TextPt, string TextEn,
    string CreditPt = "", string CreditEn = "", bool Important = false)
{
    public string Credit => L.T(CreditPt, CreditEn);
    public string Title => L.T(TitlePt, TitleEn);
    public string Text => L.T(TextPt, TextEn);
}

/// <summary>
/// Tela depois de cada atualizacao (mostra aprovada em 2026-11-12:
/// https://claude.ai/artifact/EpnC2HpjG4dvKsRHs8uBuS): resumo e apoio, depois
/// "O que mudou". Novidade nova entra no topo de All, com a versao do main.
/// </summary>
public static class Changes
{
    /// <summary>Agradecimento no alto da primeira tela, so para quem chega nesta versao (2026-11-16).</summary>
    public const string ThanksVersion = "0.2.2";
    // Textos de agradecimento antigos (a chave em ingles fica nas tabelas das linguas): 0.2.1 "Obrigado to everyone who came from Instagram...".
    public static string Thanks => L.T(
        "Obrigado a quem está abrindo PR para ajudar. Os envios de vocês já somam mais de 100 capturas e mais de 300 aparelhos catalogados. O Joule é feito por uma pessoa só, então cada aparelho novo pode levar um tempo: acompanhe as atualizações no Instagram (@dillan.apps) e no YouTube (@dillanapps).",
        "Obrigado to everyone opening PRs to help. Your submissions already add up to over 100 captures and more than 300 catalogued devices. Joule is made by one person, so each new device can take a while: follow the updates on Instagram (@dillan.apps) and YouTube (@dillanapps).");

    public static readonly Change[] All =
    {
        // 0.2.3 (preparada em 2026-11-18, nada lancado): PlayStation so lendo (item 32) e confirmados dos envios de 17 e 18/11.
        new("0.2.3", ChangeGroup.Devices, "M6.5 5h7a5 5 0 0 1 0 10h-7a5 5 0 0 1 0-10zM6.5 8.5v3M5 10h3",
            "DualSense no Bluetooth", "DualSense on Bluetooth",
            "Controle", "Controller"),
        new("0.2.3", ChangeGroup.Devices, "M6.5 5h7a5 5 0 0 1 0 10h-7a5 5 0 0 1 0-10zM6.5 8.5v3M5 10h3",
            "DualShock 4", "DualShock 4",
            "Controle", "Controller"),
        new("0.2.3", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "HyperX Cloud Alpha 2 Wireless", "HyperX Cloud Alpha 2 Wireless",
            "Fone", "Headset"),
        new("0.2.3", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "AULA F75 no receptor", "AULA F75 on the receiver",
            "Teclado", "Keyboard"),
        new("0.2.3", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "Pulsar TenZ Signature Edition", "Pulsar TenZ Signature Edition",
            "Mouse, receptor Pulsar 8K", "Mouse, Pulsar 8K receiver"),
        new("0.2.3", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "SteelSeries Arctis Nova 3P / 3X", "SteelSeries Arctis Nova 3P / 3X",
            "Fone", "Headset"),
        new("0.2.3", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "Logitech G733", "Logitech G733",
            "Fone", "Headset"),
        new("0.2.3", ChangeGroup.Fixes, "M6 3h6l3 3v11H6zM12 3v3h3",
            "Envio mais claro", "Clearer submissions",
            "Marca sem permissão: o arquivo diz que nada foi perguntado.", "Brand not allowed: the file says nothing was asked."),
        new("0.2.2", ChangeGroup.Devices, "M10 2.5a4.5 4.5 0 0 1 4.5 4.5v6a4.5 4.5 0 0 1-9 0V7A4.5 4.5 0 0 1 10 2.5zM10 5.5v3",
            "ATK 1K DONGLE-PAW", "ATK 1K DONGLE-PAW",
            "Mouse", "Mouse", "Obrigado, @DiogoLss", "Thanks, @DiogoLss"),
        new("0.2.2", ChangeGroup.Devices, "M4 12v-2a6 6 0 0 1 12 0v2M4 12h2.5v4H4zM13.5 12H16v4h-2.5z",
            "ATK Mercury I / M1", "ATK Mercury I / M1",
            "Fone", "Headset", "Obrigado, @DiogoLss", "Thanks, @DiogoLss"),
        new("0.2.2", ChangeGroup.Devices, "M10 2.5a4.5 4.5 0 0 1 4.5 4.5v6a4.5 4.5 0 0 1-9 0V7A4.5 4.5 0 0 1 10 2.5zM10 5.5v3",
            "ATK A9 Ultra V2", "ATK A9 Ultra V2",
            "Mouse", "Mouse", "Obrigado, @BatuhanUyurca", "Thanks, @BatuhanUyurca"),
        new("0.2.2", ChangeGroup.Devices, "M10 2.5a4.5 4.5 0 0 1 4.5 4.5v6a4.5 4.5 0 0 1-9 0V7A4.5 4.5 0 0 1 10 2.5zM10 5.5v3",
            "Attack Shark V6", "Attack Shark V6",
            "Mouse", "Mouse", "Obrigado, @Ali-Fani", "Thanks, @Ali-Fani"),
        new("0.2.2", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "AULA AU75", "AULA AU75",
            "Teclado", "Keyboard"),
        new("0.2.2", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "Razer Basilisk V3 Pro 35K", "Razer Basilisk V3 Pro 35K",
            "Mouse", "Mouse"),
        new("0.2.2", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "SteelSeries Arctis Nova 7 Gen 2", "SteelSeries Arctis Nova 7 Gen 2",
            "Fone", "Headset"),
        new("0.2.2", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "Lamzu Maya X", "Lamzu Maya X",
            "Mouse", "Mouse"),
        new("0.2.2", ChangeGroup.Confirmed, "M4.5 10.5l3.5 3.5 7.5-8",
            "Dark Project Novus", "Dark Project Novus",
            "Mouse, receptor VGN/VXE", "Mouse, VGN/VXE receiver"),
        new("0.2.2", ChangeGroup.Languages, "M10 3a7 7 0 1 0 7 7M10 3c2 2 3 4.5 3 7s-1 5-3 7c-2-2-3-4.5-3-7s1-5 3-7zM3 10h14",
            "繁體中文, novo", "繁體中文, new",
            "Para trocar, vá em Configurações.", "To change it, go to Settings.", "謝謝，@mochi0820！", "謝謝，@mochi0820！"),
        new("0.2.2", ChangeGroup.Languages, "M10 3a7 7 0 1 0 7 7M10 3c2 2 3 4.5 3 7s-1 5-3 7c-2-2-3-4.5-3-7s1-5 3-7zM3 10h14",
            "Français, revu", "Français, revu",
            "Para trocar, vá em Configurações.", "To change it, go to Settings.", "Merci, @TukaLeHibou !", "Merci, @TukaLeHibou !"),
        new("0.2.2", ChangeGroup.Fixes, "M10 3a7 7 0 1 0 0 14a7 7 0 1 0 0-14M10 6.5V10l2.5 1.5",
            "Hora no seu formato", "Time in your format",
            "Segue o formato do Windows, 12 h ou 24 h.", "Follows your Windows format, 12-hour or 24-hour.", "Obrigado, Yim", "Thanks, Yim"),
        new("0.2.2", ChangeGroup.Fixes, "M4 5h12v11H4zM4 8.5h12M7.5 3v4M12.5 3v4",
            "Data certa no envio", "The right date on submissions",
            "O arquivo sai com a data comum em qualquer calendário.", "The file uses the standard date on any calendar."),
        new("0.2.1", ChangeGroup.Languages, "M10 3a7 7 0 1 0 7 7M10 3c2 2 3 4.5 3 7s-1 5-3 7c-2-2-3-4.5-3-7s1-5 3-7zM3 10h14",
            "En français", "En français",
            "Para trocar, vá em Configurações.", "To change it, go to Settings.", "Merci, @TukaLeHibou !", "Merci, @TukaLeHibou !"),
        new("0.2.1", ChangeGroup.Languages, "M10 3a7 7 0 1 0 7 7M10 3c2 2 3 4.5 3 7s-1 5-3 7c-2-2-3-4.5-3-7s1-5 3-7zM3 10h14",
            "На русском", "На русском",
            "Para trocar, vá em Configurações.", "To change it, go to Settings.", "Спасибо, @wrldwideme!", "Спасибо, @wrldwideme!"),
        new("0.2.1", ChangeGroup.Devices, "M4 12v-2a6 6 0 0 1 12 0v2M4 12h2.5v4H4zM13.5 12H16v4h-2.5z",
            "Razer BlackShark V3", "Razer BlackShark V3",
            "A bateria do fone pelo dongle, conferida no aparelho de quem fez.", "The headset battery through the dongle, checked on the contributor’s own headset.", "Obrigado, @Ali-Fani", "Thanks, @Ali-Fani"),
        new("0.2.1", ChangeGroup.Devices, "M7 3h6a3 3 0 0 1 3 3v8a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3zM10 3v5",
            "AULA F75 e receptores AJAZZ 8K e Zaopin", "AULA F75 and the AJAZZ 8K and Zaopin receivers",
            "Mais aparelhos lidos, com o código vindo da comunidade.", "More devices read, with code from the community.", "Obrigado, @wrldwideme", "Thanks, @wrldwideme"),
        new("0.2.1", ChangeGroup.Fixes, "M3 7.5h12.5v5H3zM15.5 9h1.5v2h-1.5M5.5 10l2 1.5 3.5-3",
            "Verde quando completa", "Green when full",
            "Aparelho que informa a carga fica verde no widget e na bandeja ao chegar a 100%.", "Devices that report charging turn green on the widget and the tray when they reach 100%.", "Obrigado, Daniel Palma", "Thanks, Daniel Palma"),
        new("0.2.1", ChangeGroup.Fixes, "M16 10a6 6 0 1 1-1.8-4.3M16 3v3.5h-3.5",
            "Número em dia", "Up-to-date numbers",
            "Abrir o painel lê de novo quando a última leitura passou de uma hora.", "Opening the panel reads again when the last reading is over an hour old."),
        new("0.2.1", ChangeGroup.Fixes, "M10 14V4M6 8l4-4 4 4M4 16h12",
            "Enviar um aparelho, refeito", "Submit a device, reworked",
            "Telas novas, com Voltar em cada passo. Você marca qual é o seu aparelho e a bateria que ele mostra, e o relato sai na língua do app, com menos dados pessoais e os pedidos parecidos para apoiar.", "New screens, with Back on every step. You mark which device is yours and the battery it shows, and the report follows the app language, carries less personal data and shows similar requests to support."),
    };

    /// <summary>Novidades mais novas que a versao de antes ("" = veio de antes da tela existir: todas), mais as importantes da versao de antes.</summary>
    public static IReadOnlyList<Change> Since(string last) =>
        Version.TryParse(last, out var from)
            ? All.Where(c => Version.Parse(c.Version) > from || (c.Important && Version.Parse(c.Version) == from)).ToList()
            : All;

    /// <summary>
    /// Versao de onde veio, quando a tela deve abrir agora; null quando nao.
    /// Abre uma vez por versao nova, nunca na primeira instalacao (as boas-vindas ja explicam).
    /// </summary>
    public static string? UpdatedFrom(string lastVersion, string currentVersion, bool onboardingDone) =>
        lastVersion == currentVersion || !onboardingDone ? null : lastVersion;
}

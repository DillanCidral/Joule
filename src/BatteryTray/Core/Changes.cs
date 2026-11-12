namespace BatteryTray.Core;

/// <summary>Uma novidade da tela "O que mudou" (2026-11-12). Glyph = traco 20x20, como os desenhos das telas.</summary>
public sealed record Change(string Version, string Glyph, string TitlePt, string TitleEn, string TextPt, string TextEn)
{
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
    public static readonly Change[] All =
    {
        new("0.2.0", "M10 3a7 7 0 1 0 7 7M10 3c2 2 3 4.5 3 7s-1 5-3 7c-2-2-3-4.5-3-7s1-5 3-7zM3 10h14",
            "Em inglês também", "In English too",
            "O Joule segue o idioma do Windows. Para trocar, vá em Configurações.", "Joule follows the Windows language. To change it, go to Settings."),
        new("0.2.0", "M10 3.5v9M6.5 9 10 12.5 13.5 9M4 16h12",
            "Atualizações pelo GitHub", "Updates from GitHub",
            "As versões novas chegam direto da página oficial do Joule.", "New versions come straight from Joule’s official page."),
        new("0.2.0", "M10 16.5s-6-3.6-6-8A3.5 3.5 0 0 1 10 6a3.5 3.5 0 0 1 6 2.5c0 4.4-6 8-6 8z",
            "Resumo depois de atualizar", "Summary after updating",
            "O que o Joule já fez por você, contado só neste PC.", "What Joule has done for you, counted only on this PC."),
    };

    /// <summary>Novidades mais novas que a versao de antes ("" = veio de antes da tela existir: todas).</summary>
    public static IReadOnlyList<Change> Since(string last) =>
        Version.TryParse(last, out var from)
            ? All.Where(c => Version.Parse(c.Version) > from).ToList()
            : All;

    /// <summary>
    /// Versao de onde veio, quando a tela deve abrir agora; null quando nao.
    /// Abre uma vez por versao nova, nunca na primeira instalacao (as boas-vindas ja explicam).
    /// </summary>
    public static string? UpdatedFrom(string lastVersion, string currentVersion, bool onboardingDone) =>
        lastVersion == currentVersion || !onboardingDone ? null : lastVersion;
}

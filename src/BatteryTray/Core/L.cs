using System.Globalization;

namespace BatteryTray.Core;

/// <summary>
/// Idioma da interface (2026-11-12): portugues ou ingles, cada texto com as
/// duas versoes lado a lado onde e' usado (L.T no codigo, {ui:Tr} no XAML).
/// Decidido ao abrir o app; trocar nas Configuracoes vale ao reabrir.
/// O log continua em portugues (e' para o Dillan, nao para quem usa).
/// </summary>
public static class L
{
    public static bool En { get; private set; }

    /// <summary>"auto" segue o idioma do Windows: portugues so se ele for portugues.</summary>
    public static void Set(string? choice) =>
        En = choice switch
        {
            "en" => true,
            "pt" => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "pt",
        };

    public static string T(string pt, string en) => En ? en : pt;

    /// <summary>Plural simples: um / varios.</summary>
    public static string N(int n, string ptOne, string ptMany, string enOne, string enMany) =>
        n == 1 ? T(ptOne, enOne) : T(ptMany, enMany);
}

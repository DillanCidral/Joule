using System.Globalization;

namespace BatteryTray.Core;

/// <summary>
/// Idioma da interface: portugues, ingles, frances, russo ou chines tradicional.
/// Decidido ao abrir o app; trocar nas Configuracoes vale ao reabrir.
/// </summary>
public static class L
{
    public static string Code { get; private set; } = "pt";
    public static bool En => Code == "en";
    public static bool Ru => Code == "ru";

    /// <summary>"auto" segue o idioma do Windows; os demais usam ingles.</summary>
    public static string Resolve(string? choice) =>
        choice switch
        {
            "pt" or "en" or "fr" or "ru" or "zh-TW" => choice,
            _ => CultureInfo.CurrentUICulture.Name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
                 || CultureInfo.CurrentUICulture.Name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
                 || CultureInfo.CurrentUICulture.Name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
                 || CultureInfo.CurrentUICulture.Name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
                ? "zh-TW"
                : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
                {
                    "pt" => "pt",
                    "fr" => "fr",
                    "ru" => "ru",
                    _ => "en",
                },
        };

    public static void Set(string? choice) => Code = Resolve(choice);

    public static string T(string pt, string en) => Code switch
    {
        "pt" => pt,
        "fr" => French.Of(en),
        "ru" => RussianTranslations.Translate(en),
        "zh-TW" => TraditionalChinese.Of(en),
        _ => en,
    };

    /// <summary>Plural simples: um / varios.</summary>
    public static string N(int n, string ptOne, string ptMany, string enOne, string enMany) =>
        n == 1 ? T(ptOne, enOne) : T(ptMany, enMany);
}
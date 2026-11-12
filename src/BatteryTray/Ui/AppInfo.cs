namespace BatteryTray.Ui;

/// <summary>
/// Nome que aparece para o usuario ("Joule", identidade de 26/10/2026):
/// trocar aqui troca em todo lugar visivel. Exe, pasta de dados, autostart e
/// mutex tambem viraram "Joule" (AppPaths e Autostart migram o "BatteryTray"
/// antigo); so os namespaces do codigo continuam BatteryTray.
///
/// Canal de teste (`-p:JouleChannel=teste`, ver tools/release.ps1): outro
/// app de verdade -- Id, pasta, mutex e autostart proprios, entao roda ao lado
/// do main -- e diferente a olho: nome "Joule Teste", violeta no lugar do
/// amarelo e um ponto violeta no icone da bandeja.
/// </summary>
public static class AppInfo
{
#if JOULE_TESTE
    public static readonly bool IsTest = true;
    public const string Name = "Joule Teste";
    /// <summary>Pasta de dados, mutex, evento e valor do autostart.</summary>
    public const string Id = "JouleTeste";
    private const string Channel = "teste";
#else
    public static readonly bool IsTest = false;
    public const string Name = "Joule";
    public const string Id = "Joule";
    private const string Channel = "main";
#endif

    /// <summary>
    /// De onde o atualizador baixa (Velopack). O main, do GitHub Releases do
    /// repositorio oficial (item 4, 2026-11-12); o Teste continua so neste PC, na
    /// pasta escrita pelo tools/release.ps1, que passa o caminho na compilacao
    /// (JouleTestFeed). O --selftest do diag confere os dois.
    /// </summary>
#if JOULE_TESTE
    public static readonly string UpdateFeed = TestFeedOrEmpty(typeof(AppInfo).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "JouleTestFeed")?.Value);
#else
    public const string UpdateFeed = "https://github.com/DillanCidral/Joule";
#endif

    /// <summary>O Teste so aceita uma pasta local terminada em releases\teste: nada de internet nem de rede. Vazio = sem atualizador.</summary>
    public static string TestFeedOrEmpty(string? feed) =>
        feed is { Length: > 2 } && feed[1] == ':' && Path.IsPathRooted(feed) && !feed.Contains("://")
        && feed.TrimEnd('\\').EndsWith(@"\releases\teste", StringComparison.OrdinalIgnoreCase) ? feed : "";

    /// <summary>
    /// Onde o "Mandar meu aparelho" abre o relato.
    /// shortcut: aponta para o repositorio privado de hoje (so o dono abre); troca para o publico no item 3 da fila.
    /// </summary>
    public const string Repository = "github.com/DillanCidral/Joule";
    public const string NewIssueUrl = "https://" + Repository + "/issues/new";

    /// <summary>Versao do Fonts/Geist-SemiBold.ttf (tabela name); trocar junto com o arquivo.</summary>
    public const string GeistVersion = "1.900";

    /// <summary>
    /// Link de apoio (Stripe) da tela depois de atualizar. Vazio esconde o botao e a frase do cafe.
    /// So aceita https://buy.stripe.com/ (--selftest do diag).
    /// </summary>
    public const string SupportUrl = "https://buy.stripe.com/bJeeVe5yq5Pub7bfz8bZe00";

    /// <summary>E-mail "dev" do Dillan, público. Vazio esconde o caminho por e-mail.</summary>
    public const string ContactEmail = "dillan.apps@gmail.com";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
}

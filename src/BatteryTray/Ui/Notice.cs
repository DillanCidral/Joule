using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>Consent: marca nao testada conectou e espera licenca para receber a pergunta de bateria (2026-11-12).</summary>
public enum NoticeKind { Reading, Connected, Disconnected, Low, Consent }

/// <summary>
/// Um aviso: aparelho entrou (lendo, depois com o resultado), saiu, ou ficou
/// com bateria baixa. <see cref="Value"/> e' so o que foi lido AGORA
/// ("74%", "Medio", "no cabo"); null = sem dado, nunca um numero de cache.
/// </summary>
public sealed record Notice(string Key, string Name, DeviceKind Kind, NoticeKind What,
    string? Value = null, bool Low = false, int? Percent = null, string? Vid = null)
{
    /// <summary>
    /// Som so quando a bateria foi lida (teste de fogo de 2026-11-10): o
    /// Windows ja toca o dele ao plugar e ao tirar, e som em tudo incomodava.
    /// Sem dado, sem suporte, lendo ou desconectou: so a animacao.
    /// </summary>
    public Chime? Sound => What switch
    {
        NoticeKind.Reading or NoticeKind.Disconnected or NoticeKind.Consent => null,
        _ when Low => Chime.Low,
        _ when Value is null => null,
        _ => Chime.Found,
    };

    /// <summary>Linha de baixo do aviso no widget.</summary>
    public string State => What switch
    {
        NoticeKind.Reading => L.T("Conectado · Lendo…", "Connected · Reading…"),
        NoticeKind.Disconnected => L.T("Desconectado", "Disconnected"),
        NoticeKind.Consent => L.T("Para mostrar a bateria, o Joule precisa enviar uma pergunta a este aparelho. A marca ainda não foi testada com o Joule.", "To show the battery, Joule needs to send this device a question. This brand hasn’t been tested with Joule yet."),
        NoticeKind.Low => L.T($"{Value} · bateria baixa", $"{Value} · low battery"),
        _ when Value is null => L.T("Conectado · sem dado de bateria", "Connected · no battery data"),
        _ when Low => L.T($"{Value} · bateria baixa", $"{Value} · low battery"),
        _ => L.T($"Conectado · {Value}", $"Connected · {Value}"),
    };

    /// <summary>Titulo e texto da notificacao do Windows.</summary>
    public (string Title, string Text) Balloon => What switch
    {
        NoticeKind.Disconnected => (L.T($"{Name} desconectado", $"{Name} disconnected"), L.T("O último valor continua guardado no painel.", "The last value stays saved in the panel.")),
        NoticeKind.Low => (L.T("Bateria baixa", "Low battery"), $"{Name}: {Value}"),
        NoticeKind.Consent => (L.T($"{Name} conectado", $"{Name} connected"), L.T("O Joule precisa de permissão para ler a bateria deste aparelho. Clique para abrir o painel.", "Joule needs permission to read this device’s battery. Click to open the panel.")),
        _ when Value is null => (L.T($"{Name} conectado", $"{Name} connected"), L.T("Sem dado de bateria.", "No battery data.")),
        _ when Low => (L.T($"{Name} conectado", $"{Name} connected"), L.T($"Bateria baixa: {Value}", $"Low battery: {Value}")),
        _ => (L.T($"{Name} conectado", $"{Name} connected"), $"Bateria: {Value}"),
    };
}

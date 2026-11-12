using BatteryTray.Interop;

namespace BatteryTray.Core;

/// <summary>
/// Provider que mantem um listener passivo aberto enquanto o app roda, para
/// dispositivos que so falam quando o software do fabricante pergunta.
///
/// Isto NAO e' polling: nada e' enviado ao dispositivo e nao ha laco de
/// verificacao periodica. E' um unico handle aberto, escutando o que ja
/// trafega no barramento. O ciclo de vida e' controlado pelo ReadingService.
/// </summary>
public interface IPassiveListenerProvider
{
    /// <summary>
    /// Abre o listener se ainda nao houver um vivo. Chamado apenas em momentos
    /// de leitura sob demanda (abertura do app, login, botao Atualizar).
    /// </summary>
    void EnsureListening(IReadOnlyList<HidCollection> collections);

    /// <summary>Fecha o listener (um unico CancelIoEx). Chamado ao sair.</summary>
    void StopListening();

    /// <summary>Handle de escuta aberto e vivo agora.</summary>
    bool IsListening { get; }

    /// <summary>
    /// Disparado na thread do leitor quando chega um percentual DIFERENTE do
    /// anterior. Quem assina decide o que fazer; o listener nao muda de ritmo.
    /// </summary>
    event Action? ValueChanged;
}

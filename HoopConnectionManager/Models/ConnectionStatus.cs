namespace HoopConnectionManager.Models;

/// <summary>
/// Status possíveis de uma conexão Hoop.
/// </summary>
public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Reconnecting,
    Connected,
    Error
}

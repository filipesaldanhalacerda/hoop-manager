using HoopConnectionManager.Models;

namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Mantém lista de túneis Hoop ativos, portas utilizadas e processos em execução.
/// </summary>
public interface IConnectionService
{
    IReadOnlyDictionary<string, ActiveTunnel> ActiveTunnels { get; }
    event EventHandler<ActiveTunnelsChangedEventArgs>? ActiveTunnelsChanged;
    Task<ActiveTunnel> ConnectAsync(string connectionName, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string connectionName);
    Task DisconnectAllAsync();
    bool IsConnected(string connectionName);
}

public sealed class ActiveTunnelsChangedEventArgs : EventArgs
{
    public string ConnectionName { get; }
    public ConnectionStatus Status { get; }
    public string? Detail { get; }
    public bool IsConnected => Status == ConnectionStatus.Connected;

    public ActiveTunnelsChangedEventArgs(string connectionName, ConnectionStatus status, string? detail = null)
    {
        ConnectionName = connectionName;
        Status = status;
        Detail = detail;
    }
}

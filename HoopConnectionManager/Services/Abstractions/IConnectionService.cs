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
    public bool IsConnected { get; }

    public ActiveTunnelsChangedEventArgs(string connectionName, bool isConnected)
    {
        ConnectionName = connectionName;
        IsConnected = isConnected;
    }
}

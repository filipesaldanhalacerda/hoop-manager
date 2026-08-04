using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do gerenciador de túneis Hoop ativos.
/// </summary>
public sealed class ConnectionService : IConnectionService
{
    private readonly IHoopService _hoopService;
    private readonly ILoggerService _logger;
    private readonly Dictionary<string, ActiveTunnel> _tunnels = new();
    private readonly object _lock = new();

    public IReadOnlyDictionary<string, ActiveTunnel> ActiveTunnels
    {
        get
        {
            lock (_lock)
            {
                return _tunnels.ToDictionary(x => x.Key, x => x.Value);
            }
        }
    }

    public event EventHandler<ActiveTunnelsChangedEventArgs>? ActiveTunnelsChanged;

    public ConnectionService(IHoopService hoopService, ILoggerService logger)
    {
        _hoopService = hoopService;
        _logger = logger;
    }

    public async Task<ActiveTunnel> ConnectAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        if (IsConnected(connectionName))
        {
            throw new InvalidOperationException($"Já existe um túnel ativo para '{connectionName}'.");
        }

        _logger.LogInformation($"Conectando a '{connectionName}'.");
        var tunnel = await _hoopService.ConnectAsync(connectionName, cancellationToken);

        if (tunnel.Status != ConnectionStatus.Connected || tunnel.Credentials is null)
        {
            tunnel.Dispose();
            throw new InvalidOperationException(tunnel.ErrorMessage ?? $"O túnel '{connectionName}' não ficou disponível.");
        }

        lock (_lock)
        {
            _tunnels[connectionName] = tunnel;
        }

        tunnel.Process?.WaitForExitAsync(CancellationToken.None).ContinueWith(_ =>
        {
            CleanupTunnel(connectionName);
        }, TaskScheduler.Default);

        ActiveTunnelsChanged?.Invoke(this, new ActiveTunnelsChangedEventArgs(connectionName, true));
        return tunnel;
    }

    public async Task DisconnectAsync(string connectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        ActiveTunnel? tunnel;
        lock (_lock)
        {
            _tunnels.TryGetValue(connectionName, out tunnel);
            if (tunnel is not null)
            {
                _tunnels.Remove(connectionName);
            }
        }

        if (tunnel is not null)
        {
            tunnel.Dispose();
            await _hoopService.DisconnectAsync(connectionName);
            _logger.LogInformation($"Túnel '{connectionName}' finalizado.");
            ActiveTunnelsChanged?.Invoke(this, new ActiveTunnelsChangedEventArgs(connectionName, false));
        }
    }

    public async Task DisconnectAllAsync()
    {
        var names = ActiveTunnels.Keys.ToList();
        foreach (var name in names)
        {
            await DisconnectAsync(name);
        }
    }

    public bool IsConnected(string connectionName)
    {
        lock (_lock)
        {
            return _tunnels.TryGetValue(connectionName, out var tunnel)
                && tunnel.Status == ConnectionStatus.Connected
                && (tunnel.Process is null || !tunnel.Process.HasExited);
        }
    }

    private void CleanupTunnel(string connectionName)
    {
        var removed = false;
        lock (_lock)
        {
            if (_tunnels.TryGetValue(connectionName, out var tunnel))
            {
                tunnel.Status = ConnectionStatus.Disconnected;
                _tunnels.Remove(connectionName);
                removed = true;
            }
        }

        if (removed)
        {
            ActiveTunnelsChanged?.Invoke(this, new ActiveTunnelsChangedEventArgs(connectionName, false));
            _logger.LogInformation($"Túnel '{connectionName}' encerrou naturalmente.");
        }
    }
}

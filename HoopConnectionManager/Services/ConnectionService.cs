using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Gerencia túneis Hoop ativos e recupera conexões encerradas inesperadamente.
/// </summary>
public sealed class ConnectionService : IConnectionService
{
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    private readonly IHoopService _hoopService;
    private readonly ILoggerService _logger;
    private readonly Dictionary<string, ActiveTunnel> _tunnels = new();
    private readonly HashSet<string> _desiredConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _reconnectCancellations = new(StringComparer.OrdinalIgnoreCase);
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

        lock (_lock)
        {
            if (_desiredConnections.Contains(connectionName))
            {
                throw new InvalidOperationException($"A conexão '{connectionName}' já está ativa ou sendo restabelecida.");
            }

            // Reserva o nome antes de iniciar o processo para impedir dois cliques simultâneos.
            _desiredConnections.Add(connectionName);
        }

        _logger.LogInformation($"Conectando a '{connectionName}'.");
        ActiveTunnel tunnel;
        try
        {
            tunnel = await CreateConnectedTunnelAsync(connectionName, cancellationToken);
        }
        catch
        {
            lock (_lock)
            {
                _desiredConnections.Remove(connectionName);
            }

            throw;
        }

        var accepted = false;
        lock (_lock)
        {
            if (_desiredConnections.Contains(connectionName))
            {
                _tunnels[connectionName] = tunnel;
                accepted = true;
            }
        }

        if (!accepted)
        {
            tunnel.Dispose();
            throw new OperationCanceledException($"A conexão '{connectionName}' foi cancelada.");
        }

        StartMonitoring(tunnel);
        RaiseChanged(connectionName, ConnectionStatus.Connected);
        return tunnel;
    }

    public async Task DisconnectAsync(string connectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        ActiveTunnel? tunnel;
        CancellationTokenSource? reconnectCancellation;
        lock (_lock)
        {
            _desiredConnections.Remove(connectionName);
            _tunnels.Remove(connectionName, out tunnel);
            _reconnectCancellations.Remove(connectionName, out reconnectCancellation);
        }

        reconnectCancellation?.Cancel();
        reconnectCancellation?.Dispose();

        if (tunnel is not null)
        {
            tunnel.Dispose();
            await _hoopService.DisconnectAsync(connectionName);
        }

        _logger.LogInformation($"Túnel '{connectionName}' finalizado manualmente.");
        RaiseChanged(connectionName, ConnectionStatus.Disconnected);
    }

    public async Task DisconnectAllAsync()
    {
        List<string> names;
        lock (_lock)
        {
            names = _desiredConnections.ToList();
        }

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

    private async Task<ActiveTunnel> CreateConnectedTunnelAsync(string connectionName, CancellationToken cancellationToken)
    {
        var tunnel = await _hoopService.ConnectAsync(connectionName, cancellationToken);
        if (tunnel.Status == ConnectionStatus.Connected && tunnel.Credentials is not null)
        {
            return tunnel;
        }

        var message = tunnel.ErrorMessage ?? $"O túnel '{connectionName}' não ficou disponível.";
        tunnel.Dispose();
        throw new InvalidOperationException(message);
    }

    private void StartMonitoring(ActiveTunnel tunnel)
    {
        if (tunnel.Process is not null)
        {
            _ = MonitorTunnelAsync(tunnel);
        }
    }

    private async Task MonitorTunnelAsync(ActiveTunnel tunnel)
    {
        try
        {
            await tunnel.Process!.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // O processo já foi descartado por uma desconexão manual.
            return;
        }

        var exitCode = TryGetExitCode(tunnel);
        var shouldReconnect = false;
        lock (_lock)
        {
            if (_tunnels.TryGetValue(tunnel.ConnectionName, out var current) && ReferenceEquals(current, tunnel))
            {
                _tunnels.Remove(tunnel.ConnectionName);
                shouldReconnect = _desiredConnections.Contains(tunnel.ConnectionName);
            }
        }

        if (!shouldReconnect)
        {
            return;
        }

        tunnel.Status = ConnectionStatus.Reconnecting;
        tunnel.Dispose(); // Libera credenciais e a porta reservada antes do retry.

        var detail = exitCode is null
            ? "O processo Hoop encerrou inesperadamente."
            : $"O processo Hoop encerrou com código {exitCode}.";
        _logger.LogWarning($"{detail} Conexão: '{tunnel.ConnectionName}'.");
        RaiseChanged(tunnel.ConnectionName, ConnectionStatus.Reconnecting, detail);

        await ReconnectAsync(tunnel.ConnectionName);
    }

    private async Task ReconnectAsync(string connectionName)
    {
        var cancellation = new CancellationTokenSource();
        lock (_lock)
        {
            if (!_desiredConnections.Contains(connectionName))
            {
                cancellation.Dispose();
                return;
            }

            _reconnectCancellations[connectionName] = cancellation;
        }

        Exception? lastError = null;
        for (var attempt = 0; attempt < ReconnectDelays.Length; attempt++)
        {
            try
            {
                var delay = ReconnectDelays[attempt];
                RaiseChanged(connectionName, ConnectionStatus.Reconnecting,
                    $"Tentativa {attempt + 1} de {ReconnectDelays.Length} em {delay.TotalSeconds:0} segundos.");
                await Task.Delay(delay, cancellation.Token);

                var tunnel = await CreateConnectedTunnelAsync(connectionName, cancellation.Token);
                var accepted = false;
                lock (_lock)
                {
                    if (_desiredConnections.Contains(connectionName))
                    {
                        _tunnels[connectionName] = tunnel;
                        _reconnectCancellations.Remove(connectionName);
                        accepted = true;
                    }
                }

                if (!accepted)
                {
                    tunnel.Dispose();
                    cancellation.Dispose();
                    return;
                }

                StartMonitoring(tunnel);
                _logger.LogInformation($"Túnel '{connectionName}' restabelecido automaticamente na tentativa {attempt + 1}.");
                RaiseChanged(connectionName, ConnectionStatus.Connected, "Conexão restabelecida automaticamente.");
                cancellation.Dispose();
                return;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                cancellation.Dispose();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning($"Tentativa {attempt + 1} de reconectar '{connectionName}' falhou: {ex.Message}");
            }
        }

        lock (_lock)
        {
            _desiredConnections.Remove(connectionName);
            _reconnectCancellations.Remove(connectionName);
        }

        cancellation.Dispose();
        var finalMessage = $"Não foi possível restabelecer após {ReconnectDelays.Length} tentativas: {lastError?.Message}";
        _logger.LogError(finalMessage);
        RaiseChanged(connectionName, ConnectionStatus.Error, finalMessage);
    }

    private static int? TryGetExitCode(ActiveTunnel tunnel)
    {
        try
        {
            return tunnel.Process?.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void RaiseChanged(string connectionName, ConnectionStatus status, string? detail = null)
    {
        ActiveTunnelsChanged?.Invoke(this, new ActiveTunnelsChangedEventArgs(connectionName, status, detail));
    }
}

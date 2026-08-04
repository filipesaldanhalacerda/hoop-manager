using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;
using System.Net;
using System.Net.Sockets;

namespace HoopConnectionManager.Services;

/// <summary>
/// Gerencia túneis Hoop ativos e recupera conexões encerradas inesperadamente.
/// </summary>
public sealed class ConnectionService : IConnectionService, IDisposable
{
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    ];

    private readonly IHoopService _hoopService;
    private readonly ILoggerService _logger;
    private readonly ISessionHistoryService _sessionHistory;
    private readonly Dictionary<string, ActiveTunnel> _tunnels = new();
    private readonly HashSet<string> _desiredConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disconnectingConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _reconnectCancellations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _healthFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _healthMonitorCancellation = new();
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

    public ConnectionService(IHoopService hoopService, ILoggerService logger, ISessionHistoryService sessionHistory)
    {
        _hoopService = hoopService;
        _logger = logger;
        _sessionHistory = sessionHistory;
        _ = MonitorHealthAsync(_healthMonitorCancellation.Token);
    }

    public async Task<ActiveTunnel> ConnectAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        lock (_lock)
        {
            if (_desiredConnections.Contains(connectionName) || _disconnectingConnections.Contains(connectionName))
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
        _sessionHistory.StartSession(tunnel);
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
            _tunnels.TryGetValue(connectionName, out tunnel);
            _disconnectingConnections.Add(connectionName);
            _reconnectCancellations.Remove(connectionName, out reconnectCancellation);
        }

        reconnectCancellation?.Cancel();
        reconnectCancellation?.Dispose();

        RaiseChanged(connectionName, ConnectionStatus.Disconnecting, "Encerrando processo Hoop e validando a porta local.");

        try
        {
            if (tunnel is not null)
            {
                var port = tunnel.Credentials?.Port;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                await tunnel.StopAsync(timeout.Token);
                await _hoopService.DisconnectAsync(connectionName);

                if (port is not null)
                {
                    await WaitUntilPortIsClosedAsync(port.Value, timeout.Token);
                }
            }

            lock (_lock)
            {
                if (tunnel is not null
                    && _tunnels.TryGetValue(connectionName, out var current)
                    && ReferenceEquals(current, tunnel))
                {
                    _tunnels.Remove(connectionName);
                }
                _disconnectingConnections.Remove(connectionName);
            }

            _logger.LogInformation($"Túnel '{connectionName}' finalizado e porta local confirmada como fechada.");
            if (tunnel is not null) _sessionHistory.EndSession(tunnel.Id, "Desconectado pelo usuário");
            RaiseChanged(connectionName, ConnectionStatus.Disconnected, "Processo Hoop encerrado e porta local fechada.");
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _disconnectingConnections.Remove(connectionName);
            }
            _logger.LogError(ex, $"Falha ao confirmar a desconexão de '{connectionName}'.");
            if (tunnel is not null) _sessionHistory.EndSession(tunnel.Id, "Falha durante o encerramento");
            RaiseChanged(connectionName, ConnectionStatus.Error, "Não foi possível confirmar o encerramento do túnel.");
            throw new InvalidOperationException("Não foi possível confirmar que o túnel e a porta local foram encerrados.", ex);
        }
    }

    public async Task DisconnectAllAsync()
    {
        List<string> names;
        lock (_lock)
        {
            names = _desiredConnections.Concat(_tunnels.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        List<Exception>? failures = null;
        foreach (var name in names)
        {
            try
            {
                await DisconnectAsync(name);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException("Uma ou mais conexões não puderam ser encerradas normalmente.", failures);
    }

    public bool IsConnected(string connectionName)
    {
        lock (_lock)
        {
            return _tunnels.TryGetValue(connectionName, out var tunnel)
                && tunnel.Status is ConnectionStatus.Connected or ConnectionStatus.Degraded
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
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
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

        var detail = exitCode is null
            ? "O processo Hoop encerrou inesperadamente."
            : $"O processo Hoop encerrou com código {exitCode}.";
        tunnel.Status = ConnectionStatus.Reconnecting;
        _sessionHistory.EndSession(tunnel.Id, detail);
        tunnel.Dispose(); // Libera credenciais e a porta reservada antes do retry.
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

            if (_reconnectCancellations.ContainsKey(connectionName))
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

                if (!await _hoopService.IsAuthenticatedAsync(cancellation.Token))
                {
                    lastError = new InvalidOperationException("Sessão Hoop ou rede corporativa indisponível.");
                    _logger.LogWarning(
                        $"Reconexão de '{connectionName}' adiada: autenticação/gateway indisponível " +
                        $"na tentativa {attempt + 1} de {ReconnectDelays.Length}.");
                    continue;
                }

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
                _sessionHistory.StartSession(tunnel);
                _logger.LogInformation($"Túnel '{connectionName}' restabelecido automaticamente na tentativa {attempt + 1}.");
                RaiseChanged(connectionName, ConnectionStatus.Connected, "Conexão restabelecida automaticamente.", tunnelRecreated: true);
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
        var finalMessage =
            $"Não foi possível restabelecer após {ReconnectDelays.Length} tentativas: {lastError?.Message} " +
            "A reconexão automática foi pausada para evitar tentativas contínuas; use Conectar quando a rede voltar.";
        _logger.LogError(finalMessage);
        RaiseChanged(connectionName, ConnectionStatus.Error, finalMessage);
    }

    private static int? TryGetExitCode(ActiveTunnel tunnel)
    {
        try
        {
            return tunnel.Process?.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }

    private async Task MonitorHealthAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await CheckTunnelHealthAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"A verificação de saúde dos túneis falhou: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task CheckTunnelHealthAsync(CancellationToken cancellationToken)
    {
        List<ActiveTunnel> tunnels;
        lock (_lock)
        {
            tunnels = _tunnels.Values.ToList();
        }

        if (tunnels.Count == 0)
        {
            return;
        }

        var authenticated = await _hoopService.IsAuthenticatedAsync(cancellationToken);
        foreach (var tunnel in tunnels)
        {
            if (tunnel.Credentials is null || !IsProcessRunning(tunnel.Process))
            {
                continue;
            }

            var endpointAvailable = await CanReachLocalEndpointAsync(
                tunnel.Credentials.Host,
                tunnel.Credentials.Port,
                cancellationToken);
            var isHealthy = authenticated && endpointAvailable;
            var failureCount = 0;
            var recovered = false;
            var startRecovery = false;

            lock (_lock)
            {
                if (!_tunnels.TryGetValue(tunnel.ConnectionName, out var current) || !ReferenceEquals(current, tunnel))
                {
                    continue;
                }

                if (isHealthy)
                {
                    _healthFailures.Remove(tunnel.ConnectionName);
                    if (tunnel.Status == ConnectionStatus.Degraded)
                    {
                        tunnel.Status = ConnectionStatus.Connected;
                        recovered = true;
                    }
                }
                else
                {
                    _healthFailures.TryGetValue(tunnel.ConnectionName, out failureCount);
                    failureCount++;
                    _healthFailures[tunnel.ConnectionName] = failureCount;
                    if (failureCount >= 2)
                    {
                        tunnel.Status = ConnectionStatus.Degraded;
                    }

                    if (failureCount >= 4
                        && _desiredConnections.Contains(tunnel.ConnectionName)
                        && !_disconnectingConnections.Contains(tunnel.ConnectionName)
                        && !_reconnectCancellations.ContainsKey(tunnel.ConnectionName))
                    {
                        _tunnels.Remove(tunnel.ConnectionName);
                        _healthFailures.Remove(tunnel.ConnectionName);
                        tunnel.Status = ConnectionStatus.Reconnecting;
                        startRecovery = true;
                    }
                }
            }

            if (startRecovery)
            {
                const string detail = "O túnel permaneceu instável; iniciando recuperação progressiva controlada.";
                _logger.LogWarning($"{detail} Conexão: '{tunnel.ConnectionName}'.");
                RaiseChanged(tunnel.ConnectionName, ConnectionStatus.Reconnecting, detail);
                tunnel.Dispose();
                _sessionHistory.EndSession(tunnel.Id, "Túnel instável; recuperação automática iniciada");
                _ = ReconnectAsync(tunnel.ConnectionName);
            }
            else if (recovered)
            {
                _logger.LogInformation($"Saúde normalizada em '{tunnel.ConnectionName}'.");
                RaiseChanged(tunnel.ConnectionName, ConnectionStatus.Connected, "Processo, sessão Hoop e endpoint local estão operacionais.");
            }
            else if (!isHealthy && failureCount == 2)
            {
                var detail = !authenticated
                    ? "O processo está ativo, mas a sessão do Hoop não pôde ser confirmada."
                    : "O processo está ativo, mas a porta local não está respondendo.";
                _logger.LogWarning($"Saúde degradada em '{tunnel.ConnectionName}': {detail}");
                RaiseChanged(tunnel.ConnectionName, ConnectionStatus.Degraded, detail);
            }
        }
    }

    private static bool IsProcessRunning(System.Diagnostics.Process? process)
    {
        try
        {
            return process is not null && !process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static async Task<bool> CanReachLocalEndpointAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task WaitUntilPortIsClosedAsync(int port, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void RaiseChanged(string connectionName, ConnectionStatus status, string? detail = null, bool tunnelRecreated = false)
    {
        ActiveTunnelsChanged?.Invoke(this, new ActiveTunnelsChangedEventArgs(connectionName, status, detail, tunnelRecreated));
    }

    public void Dispose()
    {
        _healthMonitorCancellation.Cancel();
        List<ActiveTunnel> tunnels;
        List<CancellationTokenSource> cancellations;
        lock (_lock)
        {
            tunnels = _tunnels.Values.ToList();
            cancellations = _reconnectCancellations.Values.ToList();
            _tunnels.Clear();
            _desiredConnections.Clear();
            _disconnectingConnections.Clear();
            _reconnectCancellations.Clear();
            _healthFailures.Clear();
        }

        foreach (var cancellation in cancellations)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        foreach (var tunnel in tunnels)
        {
            _sessionHistory.EndSession(tunnel.Id, "Aplicativo encerrado");
            tunnel.Dispose();
        }

        _healthMonitorCancellation.Dispose();
    }
}

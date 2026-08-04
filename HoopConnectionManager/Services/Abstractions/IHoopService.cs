using HoopConnectionManager.Models;

namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Responsável por todas as interações com o executável oficial hoop.exe.
/// </summary>
public interface IHoopService
{
    string? ExecutablePath { get; }
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);
    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);
    Task<UserSession> GetSessionAsync(CancellationToken cancellationToken = default);
    Task<HoopDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
    Task<GlobalConnectivity> GetConnectivityAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Connection>> GetConnectionsAsync(CancellationToken cancellationToken = default);
    Task<ActiveTunnel> ConnectAsync(string connectionName, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string connectionName);
}

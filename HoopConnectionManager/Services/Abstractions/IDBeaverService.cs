using HoopConnectionManager.Models;

namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Responsável por localizar, configurar e abrir o DBeaver.
/// </summary>
public interface IDBeaverService
{
    Task<string?> LocateAsync(CancellationToken cancellationToken = default);
    Task OpenConnectionAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default);
    Task<bool> UpdateConnectionConfigurationAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default);
}

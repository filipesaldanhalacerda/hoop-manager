using HoopConnectionManager.Models;

namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Responsável por localizar, configurar e abrir o DBeaver.
/// </summary>
public interface IDBeaverService
{
    Task<string?> LocateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Entrega a conexão ao DBeaver. Devolve <c>false</c> quando a entrega não pôde ser
    /// confirmada — quem chama precisa avisar o usuário, porque o túnel fica ativo mas
    /// o cliente de banco não recebeu o endpoint.
    /// </summary>
    Task<bool> OpenConnectionAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default);
    Task<bool> UpdateConnectionConfigurationAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default);
}

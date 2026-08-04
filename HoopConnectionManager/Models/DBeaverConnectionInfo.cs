namespace HoopConnectionManager.Models;

/// <summary>
/// Informações de conexão para abertura automática no DBeaver.
/// </summary>
public sealed class DBeaverConnectionInfo
{
    public string ConnectionId { get; init; } = string.Empty;
    public string ConnectionName { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string DriverName { get; init; } = "PostgreSQL";
    public string PreferQueryMode { get; init; } = "simple";
}

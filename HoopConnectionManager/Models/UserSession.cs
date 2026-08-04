namespace HoopConnectionManager.Models;

/// <summary>
/// Sessão do usuário autenticado no Hoop.
/// Não armazena tokens ou credenciais.
/// </summary>
public sealed class UserSession
{
    public string? Email { get; init; }
    public bool IsAuthenticated { get; init; }
    public DateTime? AuthenticatedAt { get; init; }
    public string? Organization { get; init; }
}

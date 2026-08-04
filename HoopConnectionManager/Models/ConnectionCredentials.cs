namespace HoopConnectionManager.Models;

/// <summary>
/// Credenciais temporárias obtidas de um túnel Hoop ativo.
/// Nunca deve ser serializado ou persistido em disco.
/// </summary>
public sealed class ConnectionCredentials : IDisposable
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Username { get; init; } = string.Empty;
    private string _password = string.Empty;

    public string Password => _password;

    public ConnectionCredentials(string host, int port, string username, string password)
    {
        Host = host;
        Port = port;
        Username = username;
        _password = password;
    }

    /// <summary>
    /// Substitui a senha em memória para reduzir o tempo de vida do segredo.
    /// </summary>
    public void Dispose()
    {
        _password = string.Empty;
        // Em cenários reais sensíveis, poderíamos usar SecureString ou Span<char>.
        // Aqui simplesmente evitamos expor acidentalmente em logs/JSON.
    }

    public override string ToString() => $"Host={Host};Port={Port};Username={Username};Password=***";
}

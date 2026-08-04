namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Abstração para registro de logs locais seguros.
/// Nunca deve registrar senhas, tokens ou credenciais.
/// </summary>
public interface ILoggerService
{
    void LogInformation(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogError(Exception exception, string message);
}

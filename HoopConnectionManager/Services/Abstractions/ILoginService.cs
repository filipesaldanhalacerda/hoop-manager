namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Responsável por executar o login no Hoop e detectar a conclusão.
/// </summary>
public interface ILoginService
{
    event EventHandler? AuthenticationSucceeded;
    Task<bool> LoginAsync(CancellationToken cancellationToken = default);
}

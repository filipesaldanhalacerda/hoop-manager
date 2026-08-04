namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Gerencia a inicialização do aplicativo junto com o Windows.
/// </summary>
public interface IStartupService
{
    bool IsStartupEnabled();
    void EnableStartup();
    void DisableStartup();
}

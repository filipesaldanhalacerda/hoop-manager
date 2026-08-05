namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Responsável por executar o instalador oficial do Hoop e acompanhar o progresso.
/// </summary>
public interface IInstallerService
{
    event EventHandler<InstallerProgressEventArgs>? ProgressChanged;

    /// <summary>Executa um instalador oficial escolhido pelo usuário.</summary>
    Task<bool> InstallAsync(string scriptPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executa o script oficial embutido no aplicativo. É o caminho normal do assistente:
    /// evita que o desenvolvedor precise localizar o arquivo antes de começar.
    /// </summary>
    Task<bool> InstallBundledAsync(CancellationToken cancellationToken = default);

    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);
}

public sealed class InstallerProgressEventArgs : EventArgs
{
    public int PercentComplete { get; }
    public string Message { get; }

    public InstallerProgressEventArgs(int percentComplete, string message)
    {
        PercentComplete = percentComplete;
        Message = message;
    }
}

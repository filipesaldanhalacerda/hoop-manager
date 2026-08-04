namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Responsável por executar o instalador oficial do Hoop e acompanhar o progresso.
/// </summary>
public interface IInstallerService
{
    event EventHandler<InstallerProgressEventArgs>? ProgressChanged;
    Task<bool> InstallAsync(string scriptPath, CancellationToken cancellationToken = default);
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

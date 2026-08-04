using System.Diagnostics;
using System.IO;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do serviço de instalação do Hoop.
/// </summary>
public sealed class InstallerService : IInstallerService
{
    private readonly IHoopService _hoopService;
    private readonly ILoggerService _logger;

    public event EventHandler<InstallerProgressEventArgs>? ProgressChanged;

    public InstallerService(IHoopService hoopService, ILoggerService logger)
    {
        _hoopService = hoopService;
        _logger = logger;
    }

    public async Task<bool> InstallAsync(string scriptPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Script de instalação não encontrado.", scriptPath);
        }

        _logger.LogInformation($"Iniciando instalação via script: {scriptPath}");
        ReportProgress(0, "Iniciando instalação...");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<object?>();
        process.Exited += (_, _) => tcs.TrySetResult(null);

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _logger.LogInformation($"Installer: {e.Data}");
                ReportProgress(50, e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _logger.LogError($"Installer error: {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await tcs.Task;

        ReportProgress(100, "Instalação concluída.");
        return await IsInstalledAsync(cancellationToken);
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        return await _hoopService.IsInstalledAsync(cancellationToken);
    }

    private void ReportProgress(int percent, string message)
    {
        ProgressChanged?.Invoke(this, new InstallerProgressEventArgs(percent, message));
    }
}

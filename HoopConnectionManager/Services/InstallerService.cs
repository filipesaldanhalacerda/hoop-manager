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

    internal const string BundledScriptResource = "HoopConnectionManager.Resources.Scripts.install-hoop.ps1";

    /// <summary>
    /// Grava o script oficial embutido num arquivo temporário e o executa. O PowerShell
    /// precisa de um caminho em disco: não há como executar direto do recurso.
    /// </summary>
    public async Task<bool> InstallBundledAsync(CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"dev-access-center-install-hoop-{Guid.NewGuid():N}.ps1");

        try
        {
            await using (var resource = typeof(InstallerService).Assembly.GetManifestResourceStream(BundledScriptResource)
                ?? throw new InvalidOperationException("O script de instalação embutido não foi encontrado no aplicativo."))
            await using (var file = File.Create(scriptPath))
            {
                await resource.CopyToAsync(file, cancellationToken);
            }

            _logger.LogInformation("Executando o script de instalação oficial embutido no aplicativo.");
            return await InstallAsync(scriptPath, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning($"O script temporário de instalação não pôde ser removido: {scriptPath}");
            }
        }
    }

    public async Task<bool> InstallAsync(string scriptPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Script de instalação não encontrado.", scriptPath);
        }

        _logger.LogInformation($"Iniciando instalação via script: {scriptPath}");
        ReportProgress(0, "Iniciando instalação...");

        var extension = Path.GetExtension(scriptPath).ToLowerInvariant();
        if (extension is not (".ps1" or ".cmd" or ".bat"))
            throw new NotSupportedException("Selecione um instalador oficial .ps1, .cmd ou .bat.");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = extension == ".ps1" ? "powershell.exe" : Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (extension == ".ps1")
        {
            processStartInfo.ArgumentList.Add("-NoProfile"); processStartInfo.ArgumentList.Add("-ExecutionPolicy");
            processStartInfo.ArgumentList.Add("Bypass"); processStartInfo.ArgumentList.Add("-File"); processStartInfo.ArgumentList.Add(scriptPath);
        }
        else
        {
            processStartInfo.ArgumentList.Add("/d"); processStartInfo.ArgumentList.Add("/c"); processStartInfo.ArgumentList.Add(scriptPath);
        }

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
                ReportProgress(50, $"ERRO: {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            tcs.TrySetCanceled(cancellationToken);
        });
        await tcs.Task;
        if (process.ExitCode != 0) throw new InvalidOperationException($"O instalador terminou com o código {process.ExitCode}.");

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

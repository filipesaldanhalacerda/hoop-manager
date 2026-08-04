using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do serviço de integração com Hoop CLI.
/// </summary>
public sealed class HoopService : IHoopService
{
    private readonly ICommandRunner _commandRunner;
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;

    public string? ExecutablePath { get; private set; }

    public HoopService(ICommandRunner commandRunner, ISettingsService settingsService, ILoggerService logger)
    {
        _commandRunner = commandRunner;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();

        if (!string.IsNullOrWhiteSpace(settings.HoopExecutablePath) && File.Exists(settings.HoopExecutablePath))
        {
            ExecutablePath = settings.HoopExecutablePath;
            if (await CanExecuteAsync(cancellationToken))
            {
                return true;
            }
        }

        ExecutablePath = FindExecutableInPath();
        if (ExecutablePath is not null && await CanExecuteAsync(cancellationToken))
        {
            settings.HoopExecutablePath = ExecutablePath;
            await _settingsService.SaveAsync(settings, cancellationToken);
            return true;
        }

        ExecutablePath = null;
        return false;
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsInstalledAsync(cancellationToken))
        {
            return false;
        }

        try
        {
            var result = await RunAsync("whoami", cancellationToken: cancellationToken);
            return result.Success && !string.IsNullOrWhiteSpace(result.StandardOutput);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao verificar autenticação do Hoop.");
            return false;
        }
    }

    public async Task<UserSession> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsInstalledAsync(cancellationToken))
        {
            return new UserSession { IsAuthenticated = false };
        }

        try
        {
            var result = await RunAsync("whoami", cancellationToken: cancellationToken);
            var email = result.StandardOutput.Trim().Split('\n').FirstOrDefault();

            return new UserSession
            {
                Email = email,
                IsAuthenticated = !string.IsNullOrWhiteSpace(email),
                AuthenticatedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao obter sessão do Hoop.");
            return new UserSession { IsAuthenticated = false };
        }
    }

    public async Task<IReadOnlyList<Connection>> GetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsInstalledAsync(cancellationToken))
        {
            throw new InvalidOperationException("Hoop não está instalado.");
        }

        var result = await RunAsync("admin get connections --output json", cancellationToken: cancellationToken);
        if (!result.Success)
        {
            // Fallback para formato texto padrão.
            result = await RunAsync("admin get connections", cancellationToken: cancellationToken);
        }

        if (!result.Success)
        {
            throw new InvalidOperationException($"Falha ao listar conexões: {result.StandardError}");
        }

        return HoopOutputParser.ParseConnections(result.StandardOutput);
    }

    public async Task<ActiveTunnel> ConnectAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        if (!await IsInstalledAsync(cancellationToken))
        {
            throw new InvalidOperationException("Hoop não está instalado.");
        }

        _logger.LogInformation($"Iniciando conexão Hoop: {connectionName}");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath!,
            Arguments = $"connect {connectionName}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };

        var outputTaskCompletion = new TaskCompletionSource<string>();
        var outputBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputTaskCompletion.TrySetResult(outputBuilder.ToString());
                return;
            }

            outputBuilder.AppendLine(e.Data);
            _logger.LogInformation($"Hoop connect output: {SanitizeForLog(e.Data)}");
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _logger.LogError($"Hoop connect error: {SanitizeForLog(e.Data)}");
            }
        };

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Processo já encerrado.
            }
        });

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var credentials = await HoopOutputParser.WaitForCredentialsAsync(
            outputTaskCompletion.Task,
            TimeSpan.FromSeconds(60),
            cancellationToken);

        var tunnel = new ActiveTunnel
        {
            ConnectionName = connectionName,
            Process = process,
            Credentials = credentials,
            Status = credentials is not null ? ConnectionStatus.Connected : ConnectionStatus.Error,
            ErrorMessage = credentials is null ? "Não foi possível extrair credenciais do túnel." : null
        };

        return tunnel;
    }

    public Task DisconnectAsync(string connectionName)
    {
        _logger.LogInformation($"Solicitação de desconexão do túnel '{connectionName}'.");
        return Task.CompletedTask;
    }

    private async Task<bool> CanExecuteAsync(CancellationToken cancellationToken)
    {
        if (ExecutablePath is null)
        {
            return false;
        }

        try
        {
            var result = await _commandRunner.RunAsync(ExecutablePath, "--version", timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao executar hoop --version.");
            return false;
        }
    }

    private async Task<CommandResult> RunAsync(
        string arguments,
        CancellationToken cancellationToken = default)
    {
        if (ExecutablePath is null)
        {
            throw new InvalidOperationException("Caminho do hoop.exe não foi localizado.");
        }

        _logger.LogInformation($"Executando: {ExecutablePath} {arguments}");
        var result = await _commandRunner.RunAsync(ExecutablePath, arguments, cancellationToken: cancellationToken);

        if (!result.Success)
        {
            _logger.LogError($"Comando falhou: {result.StandardError}");
        }

        return result;
    }

    private static string? FindExecutableInPath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in paths)
        {
            var candidate = Path.Combine(path.Trim(), ApplicationConstants.HoopExecutableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string SanitizeForLog(string input)
    {
        // Remove possíveis senhas ou tokens do log.
        return Regex.Replace(input, @"(password|token|secret|key)[:\s=]+\S+", "$1=***", RegexOptions.IgnoreCase);
    }
}

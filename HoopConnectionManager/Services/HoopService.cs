using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.Win32;

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

        foreach (var candidate in await FindExecutableCandidatesAsync(cancellationToken))
        {
            ExecutablePath = candidate;
            if (await CanExecuteAsync(cancellationToken))
            {
                settings.HoopExecutablePath = ExecutablePath;
                await _settingsService.SaveAsync(settings, cancellationToken);
                return true;
            }
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
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        processStartInfo.ArgumentList.Add("connect");
        processStartInfo.ArgumentList.Add(connectionName);

        var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };

        var credentialsCompletion = new TaskCompletionSource<ConnectionCredentials?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var outputBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                credentialsCompletion.TrySetResult(HoopOutputParser.TryParseCredentials(outputBuilder.ToString()));
                return;
            }

            outputBuilder.AppendLine(e.Data);
            var parsed = HoopOutputParser.TryParseCredentials(outputBuilder.ToString());
            if (parsed is not null) credentialsCompletion.TrySetResult(parsed);
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

        ConnectionCredentials? credentials;
        try { credentials = await credentialsCompletion.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken); }
        catch (TimeoutException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose();
            throw new TimeoutException("O Hoop não informou os dados do túnel em 60 segundos.");
        }

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
            var result = await _commandRunner.RunAsync(ExecutablePath, "version", timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
            if (result.Success)
            {
                return true;
            }

            // Mantém compatibilidade com versões/distribuições que expõem a flag padrão.
            result = await _commandRunner.RunAsync(ExecutablePath, "--version", timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
            return result.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    private async Task<IReadOnlyList<string>> FindExecutableCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = GetCurrentSearchPaths();

        foreach (var path in paths)
        {
            try
            {
                var candidate = Path.Combine(path, ApplicationConstants.HoopExecutableName);
                if (File.Exists(candidate))
                {
                    candidates.Add(candidate);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                _logger.LogWarning($"Entrada inválida no PATH ignorada: {path}");
            }
        }
        AddKnownLocations(candidates);
        AddRegistryLocations(candidates);
        try
        {
            var where = await _commandRunner.RunAsync("where.exe", "hoop", cancellationToken: cancellationToken, timeout: TimeSpan.FromSeconds(5));
            if (where.Success)
                foreach (var line in where.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    if (File.Exists(line.Trim())) candidates.Add(line.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogWarning($"Não foi possível executar 'where hoop': {ex.Message}"); }
        return candidates.ToList();
    }

    private static IReadOnlyList<string> GetCurrentSearchPaths()
    {
        // O processo pode ter sido aberto antes da instalação do Hoop. Consulta também
        // os valores atuais do Windows para não depender do PATH herdado na inicialização.
        var pathValues = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
        };

        return pathValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(path => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddKnownLocations(ISet<string> candidates)
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        var relativePaths = new[] { "hoop.exe", Path.Combine("Hoop", "hoop.exe"), Path.Combine(".hoop", "hoop.exe"), Path.Combine("bin", "hoop.exe") };
        foreach (var root in roots.Where(x => !string.IsNullOrWhiteSpace(x)))
            foreach (var relative in relativePaths)
            { var path = Path.Combine(root, relative); if (File.Exists(path)) candidates.Add(path); }
    }

    private static void AddRegistryLocations(ISet<string> candidates)
    {
        var keys = new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\hoop.exe", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\hoop.exe", @"SOFTWARE\Hoop" };
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var name in keys)
                try
                {
                    using var key = hive.OpenSubKey(name);
                    var raw = key?.GetValue(null) as string ?? key?.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var path = Directory.Exists(raw) ? Path.Combine(raw, "hoop.exe") : raw;
                    if (File.Exists(path)) candidates.Add(path);
                }
                catch (Exception) { }
    }

    private static string SanitizeForLog(string input)
    {
        // Remove possíveis senhas ou tokens do log.
        return Regex.Replace(input, @"(password|token|secret|key)[:\s=]+\S+", "$1=***", RegexOptions.IgnoreCase);
    }
}

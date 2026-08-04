using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
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
    private readonly Func<bool> _isNetworkAvailable;
    private readonly object _portLock = new();
    private readonly HashSet<int> _reservedPorts = [];
    private bool? _supportsUserInfo;

    public string? ExecutablePath { get; private set; }

    public HoopService(ICommandRunner commandRunner, ISettingsService settingsService, ILoggerService logger)
        : this(commandRunner, settingsService, logger, NetworkInterface.GetIsNetworkAvailable)
    {
    }

    public HoopService(ICommandRunner commandRunner, ISettingsService settingsService, ILoggerService logger, Func<bool> isNetworkAvailable)
    {
        _commandRunner = commandRunner;
        _settingsService = settingsService;
        _logger = logger;
        _isNetworkAvailable = isNetworkAvailable;
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
                await _settingsService.UpdateAsync(value => value.HoopExecutablePath = ExecutablePath, cancellationToken);
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
            if (_supportsUserInfo is not false)
            {
                var userInfo = await RunAsync("admin get userinfo", cancellationToken: cancellationToken, logFailure: false);
                if (userInfo.Success)
                {
                    _supportsUserInfo = true;
                    return true;
                }

                DisableUserInfoWhenUnsupported(userInfo);
            }

            // Last-resort compatibility for older vendor builds.
            var result = await RunAsync("admin get connections", cancellationToken: cancellationToken);
            return result.Success;
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
            if (_supportsUserInfo is not false)
            {
                var userInfo = await RunAsync("admin get userinfo", cancellationToken: cancellationToken, logFailure: false);
                if (userInfo.Success)
                {
                    _supportsUserInfo = true;
                    var email = ExtractUserEmail(userInfo.StandardOutput);

                    return new UserSession
                    {
                        Email = email,
                        IsAuthenticated = true,
                        AuthenticatedAt = DateTime.Now
                    };
                }

                DisableUserInfoWhenUnsupported(userInfo);
            }

            // Compatibilidade com CLIs sem `admin get userinfo`: não será possível obter o
            // e-mail, mas ainda podemos confirmar que as credenciais são válidas.
            var result = await RunAsync("admin get connections", cancellationToken: cancellationToken);

            return new UserSession
            {
                IsAuthenticated = result.Success,
                AuthenticatedAt = result.Success ? DateTime.Now : null
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

        var result = await RunAsync("admin get connections --output json", cancellationToken: cancellationToken, logFailure: false);
        var connections = result.Success
            ? HoopOutputParser.ParseConnections(result.StandardOutput)
            : [];

        // Older Hoop releases may accept --output without actually producing
        // the JSON shape used by newer versions. An empty parse must therefore
        // retry the stable text command before treating the catalog as empty.
        if (!result.Success || connections.Count == 0)
        {
            result = await RunAsync("admin get connections", cancellationToken: cancellationToken);
            if (result.Success)
            {
                connections = HoopOutputParser.ParseConnections(result.StandardOutput);
            }
        }

        if (!result.Success)
        {
            throw new InvalidOperationException($"Falha ao listar conexões: {result.StandardError}");
        }

        return connections;
    }

    public async Task<ActiveTunnel> ConnectAsync(string connectionName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        if (!await IsInstalledAsync(cancellationToken))
        {
            throw new InvalidOperationException("Hoop não está instalado.");
        }

        _logger.LogInformation($"Iniciando conexão Hoop: {connectionName}");

        var localPort = ReserveLocalPort(out var usedAlternativePort);
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
        processStartInfo.ArgumentList.Add("--port");
        processStartInfo.ArgumentList.Add(localPort.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };

        var credentialsCompletion = new TaskCompletionSource<ConnectionCredentials?>(TaskCreationOptions.RunContinuationsAsynchronously);
        const int maxCapturedOutputLength = 64 * 1024;
        const int maxDiagnosticLines = 12;
        var outputBuilder = new System.Text.StringBuilder();
        var diagnosticLines = new Queue<string>();
        var outputLock = new object();
        var credentialsCaptured = 0;

        void HandleOutput(string line, bool isStandardError)
        {
            ConnectionCredentials? parsed = null;
            var sanitized = SanitizeForLog(line);

            lock (outputLock)
            {
                // Some Hoop versions write tunnel data to stderr. Combine both
                // streams in memory for parsing, but never log raw credentials.
                if (outputBuilder.Length + line.Length + Environment.NewLine.Length <= maxCapturedOutputLength)
                {
                    outputBuilder.AppendLine(line);
                }

                if (!string.IsNullOrWhiteSpace(sanitized))
                {
                    diagnosticLines.Enqueue(sanitized);
                    while (diagnosticLines.Count > maxDiagnosticLines)
                    {
                        diagnosticLines.Dequeue();
                    }
                }

                if (Volatile.Read(ref credentialsCaptured) == 0)
                {
                    parsed = HoopOutputParser.TryParseCredentials(outputBuilder.ToString());
                }
            }

            if (parsed is not null && Interlocked.Exchange(ref credentialsCaptured, 1) == 0)
            {
                credentialsCompletion.TrySetResult(parsed);
                lock (outputLock)
                {
                    outputBuilder.Clear();
                }
            }

            if (isStandardError)
            {
                _logger.LogWarning($"Hoop connect diagnostic: {sanitized}");
            }
            else
            {
                _logger.LogInformation($"Hoop connect output: {sanitized}");
            }
        }

        string GetDiagnostic()
        {
            lock (outputLock)
            {
                return string.Join(" | ", diagnosticLines);
            }
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            HandleOutput(e.Data, isStandardError: false);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                HandleOutput(e.Data, isStandardError: true);
            }
        };

        process.Exited += async (_, _) =>
        {
            // Allow the asynchronous output handlers to drain both streams.
            await Task.Delay(150).ConfigureAwait(false);
            if (Volatile.Read(ref credentialsCaptured) != 0)
            {
                return;
            }

            ConnectionCredentials? parsed;
            lock (outputLock)
            {
                parsed = HoopOutputParser.TryParseCredentials(outputBuilder.ToString());
            }
            credentialsCompletion.TrySetResult(parsed);
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

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            ReleaseReservedPort(localPort);
            process.Dispose();
            throw;
        }

        ConnectionCredentials? credentials;
        try { credentials = await credentialsCompletion.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken); }
        catch (TimeoutException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.Dispose();
            ReleaseReservedPort(localPort);
            var diagnostic = GetDiagnostic();
            var details = string.IsNullOrWhiteSpace(diagnostic)
                ? string.Empty
                : $" Detalhes do Hoop: {diagnostic}";
            throw new TimeoutException($"O Hoop não informou os dados do túnel em 60 segundos.{details}");
        }

        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process.Dispose();
            ReleaseReservedPort(localPort);
            throw;
        }

        var tunnel = new ActiveTunnel
        {
            ConnectionName = connectionName,
            Process = process,
            Credentials = credentials,
            Status = credentials is not null ? ConnectionStatus.Connected : ConnectionStatus.Error,
            ErrorMessage = credentials is null
                ? BuildConnectionErrorMessage(GetDiagnostic())
                : null,
            UsedAlternativePort = usedAlternativePort,
            ReleaseResources = () => ReleaseReservedPort(localPort)
        };

        return tunnel;
    }

    public async Task<HoopDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsInstalledAsync(cancellationToken))
        {
            return new HoopDiagnostics();
        }

        var versionResult = await RunAsync("version", cancellationToken: cancellationToken, logFailure: false);
        if (!versionResult.Success)
        {
            versionResult = await RunAsync("--version", cancellationToken: cancellationToken, logFailure: false);
        }

        var versionText = $"{versionResult.StandardOutput} {versionResult.StandardError}";
        var versionMatch = Regex.Match(versionText, @"(?<!\d)(?<version>\d+\.\d+\.\d+)(?!\d)");
        var version = versionMatch.Success ? versionMatch.Groups["version"].Value : "Não identificada";

        var environmentGateway = Environment.GetEnvironmentVariable("HOOP_APIURL");
        var usesEnvironment = !string.IsNullOrWhiteSpace(environmentGateway)
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOOP_GRPCURL"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOOP_TOKEN"));

        var gateway = environmentGateway;
        if (string.IsNullOrWhiteSpace(gateway))
        {
            var configResult = await RunAsync("config view api_url", cancellationToken: cancellationToken, logFailure: false);
            if (configResult.Success)
            {
                var url = Regex.Match(configResult.StandardOutput, "https?://[^\\s\\\"']+", RegexOptions.IgnoreCase);
                gateway = url.Success ? url.Value : configResult.StandardOutput.Trim();
            }
        }

        var supportsVersionManager = Version.TryParse(version, out var parsedVersion)
            && parsedVersion >= new Version(1, 74, 0);

        return new HoopDiagnostics
        {
            Version = version,
            GatewayUrl = string.IsNullOrWhiteSpace(gateway) ? "Não identificado" : gateway,
            ConfigurationSource = usesEnvironment ? "Variáveis de ambiente" : "Arquivo local do Hoop",
            IsAuthenticated = await IsAuthenticatedAsync(cancellationToken),
            SupportsVersionManager = supportsVersionManager
        };
    }

    public async Task<GlobalConnectivity> GetConnectivityAsync(CancellationToken cancellationToken = default)
    {
        if (!_isNetworkAvailable())
        {
            return new(GlobalConnectivityState.NoNetwork, "O Windows não detectou uma conexão de rede disponível.");
        }

        if (!await IsInstalledAsync(cancellationToken))
        {
            return new(GlobalConnectivityState.HoopDisconnected, "O executável Hoop CLI não foi localizado.");
        }

        var result = await RunAsync("admin get userinfo", cancellationToken: cancellationToken, logFailure: false);
        if (result.Success)
        {
            return new(GlobalConnectivityState.Online, "Rede, gateway e autenticação estão operacionais.");
        }

        var diagnostic = $"{result.StandardError} {result.StandardOutput}";
        if (diagnostic.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
        {
            result = await RunAsync("admin get connections", cancellationToken: cancellationToken, logFailure: false);
            if (result.Success)
            {
                return new(GlobalConnectivityState.Online, "Gateway e sessão Hoop estão operacionais.");
            }
            diagnostic = $"{result.StandardError} {result.StandardOutput}";
        }

        if (ContainsAny(diagnostic, "unauthorized", "unauthenticated", "authentication required", "token expired", "expired token", "login required", "please login", "not logged in", "invalid token", "status 401", "status 403"))
        {
            return new(GlobalConnectivityState.AuthenticationExpired, "A autenticação expirou. Use Autenticar novamente em Configurações.");
        }

        if (ContainsAny(diagnostic, "connection refused", "connection reset", "connection error", "no such host", "network is unreachable", "dial tcp", "i/o timeout", "timeout", "timed out", "deadline exceeded", "unavailable", "status 502", "status 503", "status 504"))
        {
            return new(GlobalConnectivityState.GatewayUnavailable, "A rede existe, mas o gateway Hoop não está respondendo. Verifique a VPN.");
        }

        return new(GlobalConnectivityState.HoopDisconnected, "O Hoop não confirmou uma sessão válida neste computador.");
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string BuildConnectionErrorMessage(string diagnostic)
    {
        const string fallback = "Não foi possível obter os dados do túnel.";
        return string.IsNullOrWhiteSpace(diagnostic)
            ? fallback
            : $"{fallback} Detalhes do Hoop: {diagnostic}";
    }

    private int ReserveLocalPort(out bool usedAlternativePort)
    {
        lock (_portLock)
        {
            for (var port = 5433; port <= ushort.MaxValue; port++)
            {
                if (_reservedPorts.Contains(port) || !IsPortAvailable(port))
                {
                    continue;
                }

                _reservedPorts.Add(port);
                usedAlternativePort = port != 5433;
                if (usedAlternativePort)
                {
                    _logger.LogInformation($"Porta local preferencial ocupada; selecionada automaticamente a porta {port}.");
                }
                return port;
            }
        }

        usedAlternativePort = false;
        throw new InvalidOperationException("Não há uma porta local disponível para abrir o túnel.");
    }

    private void ReleaseReservedPort(int port)
    {
        lock (_portLock)
        {
            _reservedPorts.Remove(port);
        }
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
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
        CancellationToken cancellationToken = default,
        bool logFailure = true)
    {
        if (ExecutablePath is null)
        {
            throw new InvalidOperationException("Caminho do hoop.exe não foi localizado.");
        }

        _logger.LogInformation($"Executando: {ExecutablePath} {arguments}");
        var result = await _commandRunner.RunAsync(ExecutablePath, arguments, cancellationToken: cancellationToken);

        if (!result.Success && logFailure)
        {
            _logger.LogError($"Comando falhou: {result.StandardError}");
        }

        return result;
    }

    private void DisableUserInfoWhenUnsupported(CommandResult result)
    {
        var error = $"{result.StandardError} {result.StandardOutput}";
        if (error.Contains("unknown command", StringComparison.OrdinalIgnoreCase)
            && error.Contains("userinfo", StringComparison.OrdinalIgnoreCase))
        {
            _supportsUserInfo = false;
            _logger.LogInformation("A versão instalada do Hoop não oferece 'admin get userinfo'; usando verificação compatível por catálogo.");
        }
    }

    private static string? ExtractUserEmail(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var jsonEmail = Regex.Match(
            output,
            "[\\\"](?:email|user_email)[\\\"]\\s*:\\s*[\\\"](?<email>[^\\\"]+)[\\\"]",
            RegexOptions.IgnoreCase);
        if (jsonEmail.Success)
        {
            return jsonEmail.Groups["email"].Value;
        }

        var plainEmail = Regex.Match(output, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
        return plainEmail.Success ? plainEmail.Value : null;
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
        return Regex.Replace(
            input,
            @"(?<prefix>[""']?(?:password|token|secret|api[_-]?key)[""']?\s*[:=]\s*)[""']?[^""'\s,}]+[""']?",
            "${prefix}***",
            RegexOptions.IgnoreCase);
    }
}

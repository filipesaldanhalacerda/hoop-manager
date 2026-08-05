using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.Win32;

namespace HoopConnectionManager.Services;

public sealed class DBeaverService : IDBeaverService
{
    private const string DBeaverStorePackagePrefix = "DBeaverCorp.DBeaverCE_";
    private const int RestoreWindow = 9;
    // O launcher encaminha e encerra em poucos segundos; a abertura fria costuma
    // levar bem mais em máquinas corporativas com antivírus no caminho.
    private static readonly TimeSpan ForwardingTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;
    private readonly HashSet<string> _knownConnectionNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _connectionNamesLock = new();
    private readonly SemaphoreSlim _launchLock = new(1, 1);
    private readonly Func<bool> _isDBeaverRunning;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public DBeaverService(ISettingsService settingsService, ILoggerService logger)
        : this(settingsService, logger, IsDBeaverRunning, Process.Start)
    {
    }

    /// <summary>
    /// Sobrecarga de teste: permite simular o DBeaver já aberto e observar o comando enviado.
    /// </summary>
    internal DBeaverService(
        ISettingsService settingsService,
        ILoggerService logger,
        Func<bool> isDBeaverRunning,
        Func<ProcessStartInfo, Process?> startProcess)
    {
        _settingsService = settingsService;
        _logger = logger;
        _isDBeaverRunning = isDBeaverRunning;
        _startProcess = startProcess;
    }

    public async Task<string?> LocateAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.DBeaverExecutablePath)) candidates.Add(settings.DBeaverExecutablePath);
        candidates.AddRange(new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DBeaver", "dbeaver.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "DBeaver", "dbeaver.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DBeaver", "dbeaver.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DBeaver", "dbeaver.exe")
        });
        var registry = FindFromRegistry();
        if (registry is not null) candidates.Insert(0, registry);
        var packagedApp = FindPackagedApp();
        if (packagedApp is not null) candidates.Insert(0, packagedApp);
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is null) return null;
        await _settingsService.UpdateAsync(value => value.DBeaverExecutablePath = found, cancellationToken);
        return found;
    }

    public async Task OpenConnectionAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        await OpenOrUpdateConnectionAsync(info, cancellationToken);
    }

    public async Task<bool> UpdateConnectionConfigurationAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return await OpenOrUpdateConnectionAsync(info, cancellationToken);
    }

    /// <summary>
    /// Entrega a conexão ao DBeaver e informa se o endpoint atual chegou até ele.
    /// </summary>
    private async Task<bool> OpenOrUpdateConnectionAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken)
    {
        await _launchLock.WaitAsync(cancellationToken);
        try
        {
            ValidateConnectionInfo(info);
            var path = await LocateAsync(cancellationToken)
                ?? throw new InvalidOperationException("DBeaver não encontrado. Configure o caminho manualmente.");
            var alreadyRunning = _isDBeaverRunning();
            var exists = IsKnownConnection(info.ConnectionName) || ConnectionExistsInWorkspace(info.ConnectionName);

            // O argumento -con precisa ser enviado sempre: é ele que cria a conexão ou
            // aplica a porta e a senha temporárias desta sessão. Quando já existe uma
            // janela aberta, o launcher encaminha o comando para ela e encerra sozinho,
            // de modo que nenhuma segunda janela é criada. Também não usamos
            // -reuseWorkspace: essa opção é justamente o que autoriza uma nova instância.
            var startInfo = new ProcessStartInfo
            {
                FileName = GetCommandLineExecutable(path),
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("-con");
            startInfo.ArgumentList.Add(BuildConnectionArgument(info, exists));

            cancellationToken.ThrowIfCancellationRequested();
            using var launcher = _startProcess(startInfo)
                ?? throw new InvalidOperationException("O Windows não conseguiu iniciar o DBeaver.");
            RememberConnection(info.ConnectionName);

            if (alreadyRunning)
            {
                var forwarded = await WaitForForwardingAsync(launcher, cancellationToken);
                TryActivateRunningInstance();

                if (!forwarded)
                {
                    // Nenhuma distribuição testada chega aqui; se alguma variante empacotada
                    // deixar de encaminhar, o registro identifica a máquina exata.
                    _logger.LogWarning(
                        $"O DBeaver não confirmou o encaminhamento de '{info.ConnectionName}' para a janela já aberta. " +
                        "Verifique se esta instalação abriu uma segunda janela.");
                    return false;
                }

                _logger.LogInformation(
                    $"Conexão '{info.ConnectionName}' encaminhada à janela já aberta do DBeaver com a porta {info.Port}; " +
                    "nenhuma janela adicional foi criada.");
                return true;
            }

            if (!await WaitForDBeaverStartupAsync(cancellationToken))
            {
                _logger.LogWarning($"O DBeaver foi iniciado para '{info.ConnectionName}', mas a janela não apareceu no tempo esperado.");
                return false;
            }

            TryActivateRunningInstance();
            _logger.LogInformation(exists
                ? $"Conexão existente '{info.ConnectionName}' aberta no DBeaver com a porta {info.Port}."
                : $"Conexão '{info.ConnectionName}' criada no DBeaver; a senha temporária não será salva.");
            return true;
        }
        finally
        {
            _launchLock.Release();
        }
    }

    /// <summary>
    /// Aguarda o launcher repassar o comando à instância aberta e encerrar.
    /// </summary>
    private static async Task<bool> WaitForForwardingAsync(Process launcher, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ForwardingTimeout);
            await launcher.WaitForExitAsync(timeout.Token);
            return launcher.ExitCode == 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            // Sem handle utilizável não há como confirmar; o comando foi entregue mesmo assim.
            return true;
        }
    }

    private async Task<bool> WaitForDBeaverStartupAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(200);
        for (var elapsed = TimeSpan.Zero; elapsed < StartupTimeout; elapsed += interval)
        {
            if (_isDBeaverRunning()) return true;
            await Task.Delay(interval, cancellationToken);
        }

        return _isDBeaverRunning();
    }

    /// <summary>
    /// Monta o parâmetro <c>-con</c> aceito pelo DBeaver.
    /// </summary>
    /// <remarks>
    /// RISCO ACEITO: a senha temporária trafega na linha de comando porque essa é a
    /// única interface que o DBeaver oferece para receber uma conexão pronta. Enquanto
    /// o launcher existe — poucos segundos — qualquer processo do mesmo usuário
    /// consegue lê-la via Win32_Process, e agentes de EDR costumam registrar linhas de
    /// comando. O que limita o impacto: a senha vale só para o túnel corrente, morre
    /// com ele, e <c>savePassword=false</c> impede o DBeaver de gravá-la em disco.
    /// </remarks>
    internal static string BuildConnectionArgument(DBeaverConnectionInfo info, bool exists)
    {
        var create = (!exists).ToString().ToLowerInvariant();
        return string.Join('|', new[]
        {
            $"create={create}", $"save={create}", $"name={info.ConnectionName}",
            $"driver={NormalizeDriverName(info.DriverName)}", $"host={info.Host}", $"port={info.Port}",
            $"user={info.Username}", $"password={info.Password}", "savePassword=false", "connect=true"
        });
    }

    private static string NormalizeDriverName(string driverName) =>
        driverName.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ? "postgresql" : driverName.ToLowerInvariant();

    internal static void ValidateConnectionInfo(DBeaverConnectionInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.ConnectionName) || string.IsNullOrWhiteSpace(info.Host)
            || string.IsNullOrWhiteSpace(info.Username) || info.Port is < 1 or > 65535)
            throw new ArgumentException("Os dados da conexão do DBeaver estão incompletos.", nameof(info));

        foreach (var value in new[] { info.ConnectionName, info.Host, info.Username, info.Password, info.DriverName })
            if (value.Contains('|') || value.Contains('\r') || value.Contains('\n'))
                throw new ArgumentException("Um dado da conexão contém caracteres incompatíveis com o DBeaver.", nameof(info));
    }

    private bool IsKnownConnection(string name)
    {
        lock (_connectionNamesLock) return _knownConnectionNames.Contains(name);
    }

    private void RememberConnection(string name)
    {
        lock (_connectionNamesLock) _knownConnectionNames.Add(name);
    }

    private static string GetCommandLineExecutable(string dbeaverPath)
    {
        var commandLinePath = Path.Combine(Path.GetDirectoryName(dbeaverPath)!, "dbeaverc.exe");
        return File.Exists(commandLinePath) ? commandLinePath : dbeaverPath;
    }

    private static bool ConnectionExistsInWorkspace(string connectionName)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DBeaverData");
        if (!Directory.Exists(root)) return false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "data-sources.json", SearchOption.AllDirectories))
            {
                using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                if (ContainsConnectionName(document.RootElement, connectionName)) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // O workspace pode estar bloqueado enquanto o DBeaver salva suas configurações.
        }
        return false;
    }

    private static bool ContainsConnectionName(JsonElement element, string connectionName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("name") && property.Value.ValueKind == JsonValueKind.String
                    && string.Equals(property.Value.GetString(), connectionName, StringComparison.OrdinalIgnoreCase)) return true;
                if (ContainsConnectionName(property.Value, connectionName)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                if (ContainsConnectionName(child, connectionName)) return true;
        }
        return false;
    }

    private static string? FindFromRegistry()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\dbeaver.exe");
                if (key?.GetValue(null) is string path && File.Exists(path)) return path;
            }
            catch (Exception) { }
        return null;
    }

    private static string? FindPackagedApp()
    {
        const string packagesKeyPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        try
        {
            using var packagesKey = Registry.CurrentUser.OpenSubKey(packagesKeyPath);
            if (packagesKey is null) return null;
            foreach (var packageName in packagesKey.GetSubKeyNames()
                         .Where(name => name.StartsWith(DBeaverStorePackagePrefix, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase))
            {
                using var packageKey = packagesKey.OpenSubKey(packageName);
                var packageRoot = packageKey?.GetValue("PackageRootFolder") as string;
                if (string.IsNullOrWhiteSpace(packageRoot)) continue;
                var executablePath = Path.Combine(packageRoot, "dbeaver.exe");
                if (File.Exists(executablePath)) return executablePath;
            }
        }
        catch (Exception) { }
        return null;
    }

    private static bool TryActivateRunningInstance()
    {
        var processes = Process.GetProcessesByName("dbeaver");
        if (processes.Length == 0) return false;
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var windowHandle = process.MainWindowHandle;
                    if (windowHandle == IntPtr.Zero) continue;
                    ShowWindow(windowHandle, RestoreWindow);
                    SetForegroundWindow(windowHandle);
                    break;
                }
                catch (InvalidOperationException) { }
            }
        }
        return true;
    }

    private static bool IsDBeaverRunning()
    {
        var processes = Process.GetProcessesByName("dbeaver");
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}

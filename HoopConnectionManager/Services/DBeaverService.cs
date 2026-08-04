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
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;
    private readonly HashSet<string> _knownConnectionNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _connectionNamesLock = new();

    public DBeaverService(ISettingsService settingsService, ILoggerService logger)
    {
        _settingsService = settingsService;
        _logger = logger;
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
        await OpenOrUpdateConnectionAsync(info, cancellationToken);
        return true;
    }

    private async Task OpenOrUpdateConnectionAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken)
    {
        ValidateConnectionInfo(info);
        var path = await LocateAsync(cancellationToken)
            ?? throw new InvalidOperationException("DBeaver não encontrado. Configure o caminho manualmente.");
        var running = IsDBeaverRunning();
        var exists = IsKnownConnection(info.ConnectionName) || ConnectionExistsInWorkspace(info.ConnectionName);

        var startInfo = new ProcessStartInfo
        {
            FileName = GetCommandLineExecutable(path),
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("-reuseWorkspace");
        startInfo.ArgumentList.Add("-con");
        startInfo.ArgumentList.Add(BuildConnectionArgument(info, exists));

        cancellationToken.ThrowIfCancellationRequested();
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("O Windows não conseguiu iniciar o DBeaver.");
        RememberConnection(info.ConnectionName);

        if (running)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                TryActivateRunningInstance();
            });
        }

        _logger.LogInformation(exists
            ? $"Conexão existente '{info.ConnectionName}' reutilizada no DBeaver com endpoint temporário atualizado."
            : $"Conexão '{info.ConnectionName}' criada uma única vez no DBeaver; a senha temporária não será salva.");
    }

    private static string BuildConnectionArgument(DBeaverConnectionInfo info, bool exists)
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

    private static void ValidateConnectionInfo(DBeaverConnectionInfo info)
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

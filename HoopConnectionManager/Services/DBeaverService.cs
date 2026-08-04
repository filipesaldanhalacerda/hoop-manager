using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.Win32;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do serviço de integração com DBeaver.
/// </summary>
public sealed class DBeaverService : IDBeaverService
{
    private const string DBeaverProcessName = "dbeaver";
    private const string DefaultDriver = "PostgreSQL";

    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;

    public DBeaverService(ISettingsService settingsService, ILoggerService logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<string?> LocateAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        if (!string.IsNullOrWhiteSpace(settings.DBeaverExecutablePath) && File.Exists(settings.DBeaverExecutablePath))
        {
            return settings.DBeaverExecutablePath;
        }

        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DBeaver", "dbeaver.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "DBeaver", "dbeaver.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DBeaver", "dbeaver.exe")
        };

        var registryPath = FindDBeaverPathFromRegistry();
        if (registryPath is not null)
        {
            candidates.Insert(0, registryPath);
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                settings.DBeaverExecutablePath = candidate;
                await _settingsService.SaveAsync(settings, cancellationToken);
                _logger.LogInformation($"DBeaver localizado em: {candidate}");
                return candidate;
            }
        }

        return null;
    }

    public async Task OpenConnectionAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        var dbeaverPath = await LocateAsync(cancellationToken);
        if (dbeaverPath is null)
        {
            throw new InvalidOperationException("DBeaver não encontrado. Configure o caminho manualmente.");
        }

        await UpdateConnectionConfigurationAsync(info, cancellationToken);

        if (IsDBeaverRunning())
        {
            _logger.LogInformation("DBeaver já está em execução. Atualizando conexão sem abrir nova instância.");
            BringDBeaverToFront();
            return;
        }

        var connectionString = BuildConnectionString(info);
        var startInfo = new ProcessStartInfo
        {
            FileName = dbeaverPath,
            Arguments = $"-con \"{connectionString}\"",
            UseShellExecute = true,
            CreateNoWindow = false
        };

        Process.Start(startInfo);
        _logger.LogInformation($"DBeaver aberto para conexão '{info.ConnectionName}'.");
    }

    public async Task<bool> UpdateConnectionConfigurationAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default)
    {
        try
        {
            var workspacePath = FindDBeaverWorkspace();
            if (workspacePath is null)
            {
                _logger.LogWarning("Workspace do DBeaver não encontrado.");
                return false;
            }

            var updated = await TryUpdateJsonDataSourcesAsync(workspacePath, info, cancellationToken);
            if (updated)
            {
                return true;
            }

            return TryUpdateXmlDataSources(workspacePath, info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao atualizar configuração do DBeaver.");
            return false;
        }
    }

    private static string BuildConnectionString(DBeaverConnectionInfo info)
    {
        return $"driver={info.DriverName}|host={info.Host}|port={info.Port}|user={info.Username}|password={info.Password}|name={info.ConnectionName}|database=*|prop.preferQueryMode={info.PreferQueryMode}";
    }

    private static bool IsDBeaverRunning()
    {
        return Process.GetProcessesByName(DBeaverProcessName).Any();
    }

    private static void BringDBeaverToFront()
    {
        var process = Process.GetProcessesByName(DBeaverProcessName).FirstOrDefault();
        if (process is not null && process.MainWindowHandle != IntPtr.Zero)
        {
            _ = NativeMethods.SetForegroundWindow(process.MainWindowHandle);
        }
    }

    private static string? FindDBeaverPathFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DBeaver");
            var installLocation = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                var path = Path.Combine(installLocation, "dbeaver.exe");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch (Exception)
        {
            // Ignora erros de registry.
        }

        return null;
    }

    private static string? FindDBeaverWorkspace()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var workspace = Path.Combine(appData, "DBeaverData", "workspace6");
        return Directory.Exists(workspace) ? workspace : null;
    }

    private static async Task<bool> TryUpdateJsonDataSourcesAsync(string workspacePath, DBeaverConnectionInfo info, CancellationToken cancellationToken)
    {
        var dataSourcesPath = Path.Combine(workspacePath, "General", ".dbeaver", "data-sources.json");
        if (!File.Exists(dataSourcesPath))
        {
            return false;
        }

        var json = await File.ReadAllTextAsync(dataSourcesPath, cancellationToken);
        var document = JsonNode.Parse(json) as JsonObject ?? new JsonObject();

        var connections = document["connections"] as JsonObject ?? new JsonObject();
        var safeName = Regex.Replace(info.ConnectionName, @"[^\w\-]", "_");
        var connectionKey = $"{info.ConnectionId}_{safeName}";

        var connectionNode = new JsonObject
        {
            ["provider"] = "postgresql",
            ["driver"] = "postgres-jdbc",
            ["name"] = info.ConnectionName,
            ["save-password"] = true,
            ["show-system-objects"] = true,
            ["read-only"] = false,
            ["configuration"] = new JsonObject
            {
                ["host"] = info.Host,
                ["port"] = info.Port,
                ["database"] = "postgres",
                ["user"] = info.Username,
                ["password"] = info.Password,
                ["preferQueryMode"] = info.PreferQueryMode
            }
        };

        connections[connectionKey] = connectionNode;
        document["connections"] = connections;

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(dataSourcesPath, document.ToJsonString(options), cancellationToken);

        return true;
    }

    private bool TryUpdateXmlDataSources(string workspacePath, DBeaverConnectionInfo info)
    {
        var connectionsDir = Path.Combine(workspacePath, "General", "Connections");
        if (!Directory.Exists(connectionsDir))
        {
            Directory.CreateDirectory(connectionsDir);
        }

        var safeName = Regex.Replace(info.ConnectionName, @"[^\w\-]", "_");
        var connectionDir = Path.Combine(connectionsDir, safeName);
        Directory.CreateDirectory(connectionDir);

        var dataSourcePath = Path.Combine(connectionDir, ".dbeaver-data-sources.xml");
        var document = File.Exists(dataSourcePath)
            ? XDocument.Load(dataSourcePath)
            : new XDocument(new XElement("data-sources"));

        var root = document.Root ?? new XElement("data-sources");
        var existing = root.Elements("connection")
            .FirstOrDefault(e => (string?)e.Attribute("id") == info.ConnectionId);

        existing?.Remove();

        var connectionElement = new XElement("connection",
            new XAttribute("id", info.ConnectionId),
            new XAttribute("driver", info.DriverName),
            new XAttribute("name", info.ConnectionName),
            new XElement("connection-property",
                new XAttribute("name", "host"), new XAttribute("value", info.Host)),
            new XElement("connection-property",
                new XAttribute("name", "port"), new XAttribute("value", info.Port.ToString())),
            new XElement("connection-property",
                new XAttribute("name", "user"), new XAttribute("value", info.Username)),
            new XElement("connection-property",
                new XAttribute("name", "password"), new XAttribute("value", info.Password)),
            new XElement("connection-property",
                new XAttribute("name", "preferQueryMode"), new XAttribute("value", info.PreferQueryMode))
        );

        root.Add(connectionElement);
        document.Save(dataSourcePath);

        return true;
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}

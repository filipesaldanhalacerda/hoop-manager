using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
    public DBeaverService(ISettingsService settingsService, ILoggerService logger) { _settingsService = settingsService; _logger = logger; }

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
        var registry = FindFromRegistry(); if (registry is not null) candidates.Insert(0, registry);
        var packagedApp = FindPackagedApp(); if (packagedApp is not null) candidates.Insert(0, packagedApp);
        var found = candidates.FirstOrDefault(File.Exists); if (found is null) return null;
        await _settingsService.UpdateAsync(value => value.DBeaverExecutablePath = found, cancellationToken); return found;
    }

    public async Task OpenConnectionAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (TryActivateRunningInstance())
        {
            _logger.LogInformation($"Instância existente do DBeaver reutilizada para '{info.ConnectionName}'.");
            return;
        }

        var path = await LocateAsync(cancellationToken) ?? throw new InvalidOperationException("DBeaver não encontrado. Configure o caminho manualmente.");
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        _logger.LogInformation($"DBeaver aberto para '{info.ConnectionName}'. Nenhuma configuração interna foi alterada.");
    }

    public Task<bool> UpdateConnectionConfigurationAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default)
    { _logger.LogWarning("Alteração de arquivos internos do DBeaver está desabilitada."); return Task.FromResult(false); }

    private static string? FindFromRegistry()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            try { using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\dbeaver.exe"); if (key?.GetValue(null) is string path && File.Exists(path)) return path; }
            catch (Exception) { }
        return null;
    }

    private static string? FindPackagedApp()
    {
        const string packagesKeyPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

        try
        {
            using var packagesKey = Registry.CurrentUser.OpenSubKey(packagesKeyPath);
            if (packagesKey is null)
            {
                return null;
            }

            foreach (var packageName in packagesKey.GetSubKeyNames()
                         .Where(name => name.StartsWith(DBeaverStorePackagePrefix, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase))
            {
                using var packageKey = packagesKey.OpenSubKey(packageName);
                var packageRoot = packageKey?.GetValue("PackageRootFolder") as string;
                if (string.IsNullOrWhiteSpace(packageRoot))
                {
                    continue;
                }

                var executablePath = Path.Combine(packageRoot, "dbeaver.exe");
                if (File.Exists(executablePath))
                {
                    return executablePath;
                }
            }
        }
        catch (Exception)
        {
            // O registro de pacotes pode estar restrito por política corporativa.
        }

        return null;
    }

    private static bool TryActivateRunningInstance()
    {
        var processes = Process.GetProcessesByName("dbeaver");
        if (processes.Length == 0)
        {
            return false;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var windowHandle = process.MainWindowHandle;
                    if (windowHandle != IntPtr.Zero)
                    {
                        ShowWindow(windowHandle, RestoreWindow);
                        SetForegroundWindow(windowHandle);
                        break;
                    }
                }
                catch (InvalidOperationException)
                {
                    // O processo encerrou durante a consulta; outro processo ainda
                    // pode representar a instância principal do DBeaver.
                }
            }
        }

        // Mesmo que o Windows bloqueie a ativação da janela, não iniciamos outra
        // sessão enquanto houver um processo DBeaver em execução.
        return true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}

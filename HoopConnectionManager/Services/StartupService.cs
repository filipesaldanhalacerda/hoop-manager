using System.Diagnostics;
using System.IO;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.Win32;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do gerenciamento de inicialização com Windows.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RunRegistryKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
    private readonly string _applicationName;
    private readonly string _executablePath;

    public StartupService()
    {
        _applicationName = ApplicationConstants.ApplicationName;
        _executablePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{_applicationName}.exe");
    }

    public bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey);
        var value = key?.GetValue(_applicationName) as string;
        return !string.IsNullOrWhiteSpace(value) && value == _executablePath;
    }

    public void EnableStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true)
            ?? Registry.CurrentUser.CreateSubKey(RunRegistryKey);

        key.SetValue(_applicationName, _executablePath);
    }

    public void DisableStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
        if (key?.GetValue(_applicationName) is not null)
        {
            key.DeleteValue(_applicationName);
        }
    }
}

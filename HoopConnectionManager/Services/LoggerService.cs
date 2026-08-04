using System.IO;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação simples de logger local baseado em arquivos de texto.
/// Escreve logs em %LocalAppData%\HoopConnectionManager\Logs.
/// </summary>
public sealed class LoggerService : ILoggerService
{
    private readonly string _logsDirectory;
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public LoggerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var rootDirectory = Path.Combine(appData, ApplicationConstants.ApplicationName);
        _logsDirectory = Path.Combine(rootDirectory, ApplicationConstants.LogsDirectoryName);
        _logFilePath = Path.Combine(_logsDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.txt");

        Directory.CreateDirectory(_logsDirectory);
    }

    public void LogInformation(string message) => WriteLog("INFO", message);
    public void LogWarning(string message) => WriteLog("WARN", message);
    public void LogError(string message) => WriteLog("ERROR", message);
    public void LogError(Exception exception, string message) => WriteLog("ERROR", $"{message} | Exception: {exception.Message}");

    private void WriteLog(string level, string message)
    {
        var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

        lock (_lock)
        {
            File.AppendAllText(_logFilePath, entry);
        }
    }
}

using System.IO;
using System.Text.RegularExpressions;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação simples de logger local baseado em arquivos de texto.
/// Escreve logs em %LocalAppData%\HoopConnectionManager\Logs.
/// </summary>
public sealed class LoggerService : ILoggerService
{
    private const long MaximumLogFileSize = 5 * 1024 * 1024;
    private static readonly Regex LogPattern = new(
        @"^\[(?<timestamp>[^\]]+)\] \[(?<level>[^\]]+)\] (?<message>.*)$",
        RegexOptions.Compiled);
    private readonly string _logsDirectory;
    private string _logFilePath;
    private readonly object _lock = new();

    public event EventHandler<LogEntry>? LogWritten;
    public string LogsDirectory => _logsDirectory;

    public LoggerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var rootDirectory = Path.Combine(appData, ApplicationConstants.ApplicationName);
        _logsDirectory = Path.Combine(rootDirectory, ApplicationConstants.LogsDirectoryName);
        _logFilePath = Path.Combine(_logsDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.txt");

        try
        {
            Directory.CreateDirectory(_logsDirectory);
            DeleteExpiredLogs();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logsDirectory = Path.Combine(Path.GetTempPath(), ApplicationConstants.ApplicationName, ApplicationConstants.LogsDirectoryName);
            _logFilePath = Path.Combine(_logsDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.txt");
            Directory.CreateDirectory(_logsDirectory);
        }
    }

    public void LogInformation(string message) => WriteLog("INFO", message);
    public void LogWarning(string message) => WriteLog("WARN", message);
    public void LogError(string message) => WriteLog("ERROR", message);
    public void LogError(Exception exception, string message) => WriteLog("ERROR", $"{message} | Exception: {exception.Message}");

    public IReadOnlyList<LogEntry> GetRecentEntries(int maximumCount = 500)
    {
        if (maximumCount <= 0)
        {
            return [];
        }

        string[] lines;
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_logFilePath))
                {
                    return [];
                }

                lines = File.ReadAllLines(_logFilePath);
            }
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return lines.TakeLast(maximumCount)
            .Select(ParseEntry)
            .Where(entry => entry is not null)
            .Cast<LogEntry>()
            .ToList();
    }

    private void WriteLog(string level, string message)
    {
        var timestamp = DateTime.Now;
        var logEntry = new LogEntry(timestamp, level, message);
        var entry = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

        try
        {
            lock (_lock)
            {
                RotateLogIfNecessary();
                File.AppendAllText(_logFilePath, entry);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        try
        {
            LogWritten?.Invoke(this, logEntry);
        }
        catch
        {
            // Uma falha na visualização de logs nunca pode interromper a operação principal.
        }
    }

    private static LogEntry? ParseEntry(string line)
    {
        var match = LogPattern.Match(line);
        return match.Success
            && DateTime.TryParse(match.Groups["timestamp"].Value, out var timestamp)
            ? new LogEntry(timestamp, match.Groups["level"].Value, match.Groups["message"].Value)
            : null;
    }

    private void RotateLogIfNecessary()
    {
        if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length >= MaximumLogFileSize)
        {
            _logFilePath = Path.Combine(_logsDirectory, $"log-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
        }
    }

    private void DeleteExpiredLogs()
    {
        var threshold = DateTime.UtcNow.AddDays(-14);
        foreach (var file in Directory.EnumerateFiles(_logsDirectory, "log-*.txt"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < threshold)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

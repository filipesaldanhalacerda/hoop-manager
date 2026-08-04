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
    private static readonly Regex LogPattern = new(
        @"^\[(?<timestamp>[^\]]+)\] \[(?<level>[^\]]+)\] (?<message>.*)$",
        RegexOptions.Compiled);
    private readonly string _logsDirectory;
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public event EventHandler<LogEntry>? LogWritten;
    public string LogsDirectory => _logsDirectory;

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
}

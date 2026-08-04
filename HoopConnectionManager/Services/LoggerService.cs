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
    private const long MaximumTotalLogsSize = 50 * 1024 * 1024;
    private const int MaximumLogFiles = 10;
    private static readonly Regex LogPattern = new(
        @"^\[(?<timestamp>[^\]]+)\] \[(?<level>[^\]]+)\] (?<message>.*)$",
        RegexOptions.Compiled);
    private readonly string _logsDirectory;
    private string _logFilePath;
    private DateOnly _logFileDate;
    private readonly object _lock = new();
    private readonly ISettingsService _settingsService;

    public event EventHandler<LogEntry>? LogWritten;
    public string LogsDirectory => _logsDirectory;

    public LoggerService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var rootDirectory = Path.Combine(appData, ApplicationConstants.StorageRootName);
        _logsDirectory = Path.Combine(rootDirectory, ApplicationConstants.LogsDirectoryName);
        _logFileDate = DateOnly.FromDateTime(DateTime.Now);
        _logFilePath = Path.Combine(_logsDirectory, $"log-{_logFileDate:yyyy-MM-dd}.txt");

        try
        {
            Directory.CreateDirectory(_logsDirectory);
            EnforceLogRetention();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logsDirectory = Path.Combine(Path.GetTempPath(), ApplicationConstants.StorageRootName, ApplicationConstants.LogsDirectoryName);
            _logFilePath = Path.Combine(_logsDirectory, $"log-{_logFileDate:yyyy-MM-dd}.txt");
            Directory.CreateDirectory(_logsDirectory);
            EnforceLogRetention();
        }
    }

    public void LogInformation(string message) => WriteLog("INFO", message);
    public void LogWarning(string message) => WriteLog("WARN", message);
    public void LogError(string message) => WriteLog("ERROR", message);
    public void LogError(Exception exception, string message) => WriteLog("ERROR", $"{message} | Exception: {exception.Message}");

    public LogStorageInfo GetStorageInfo()
    {
        lock (_lock)
        {
            try
            {
                var files = Directory.GetFiles(_logsDirectory, "log-*.txt")
                    .Select(path => new FileInfo(path))
                    .ToList();
                return new LogStorageInfo(
                    files.Sum(file => file.Length),
                    files.Count,
                    GetOldestEntryDate(files));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new LogStorageInfo(0, 0, null);
            }
        }
    }

    private static DateTime? GetOldestEntryDate(IEnumerable<FileInfo> files)
    {
        DateTime? oldest = null;
        foreach (var file in files)
        {
            try
            {
                var firstLine = File.ReadLines(file.FullName).FirstOrDefault();
                var entry = firstLine is null ? null : ParseEntry(firstLine);
                var date = entry?.Timestamp ?? file.LastWriteTime;
                if (oldest is null || date < oldest) oldest = date;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return oldest;
    }

    public int ClearOldLogs()
    {
        lock (_lock)
        {
            var removed = 0;
            try
            {
                foreach (var file in Directory.GetFiles(_logsDirectory, "log-*.txt"))
                {
                    if (string.Equals(file, _logFilePath, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return removed;
        }
    }

    public void ApplyRetention()
    {
        lock (_lock) EnforceLogRetention();
    }

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
                var logFileChanged = SelectCurrentLogFile() || RotateLogIfNecessary();
                File.AppendAllText(_logFilePath, entry);
                if (logFileChanged)
                {
                    EnforceLogRetention();
                }
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

    private bool SelectCurrentLogFile()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == _logFileDate)
        {
            return false;
        }

        _logFileDate = today;
        _logFilePath = Path.Combine(_logsDirectory, $"log-{today:yyyy-MM-dd}.txt");
        return true;
    }

    private bool RotateLogIfNecessary()
    {
        if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length >= MaximumLogFileSize)
        {
            _logFilePath = Path.Combine(_logsDirectory, $"log-{DateTime.Now:yyyy-MM-dd-HHmmss-fff}.txt");
            return true;
        }

        return false;
    }

    private void EnforceLogRetention()
    {
        var retentionDays = Math.Clamp(_settingsService.Load().LogRetentionDays, 1, 365);
        var threshold = DateTime.UtcNow.AddDays(-retentionDays);
        string[] logFiles;
        try
        {
            logFiles = Directory.GetFiles(_logsDirectory, "log-*.txt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in logFiles)
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

        try
        {
            var files = Directory.GetFiles(_logsDirectory, "log-*.txt")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();
            var totalSize = files.Sum(file => file.Length);

            for (var index = files.Count - 1; index >= 0; index--)
            {
                var file = files[index];
                if (string.Equals(file.FullName, _logFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (files.Count <= MaximumLogFiles && totalSize <= MaximumTotalLogsSize)
                {
                    break;
                }

                var length = file.Length;
                file.Delete();
                files.RemoveAt(index);
                totalSize -= length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A retenção será tentada novamente na próxima rotação.
        }
    }
}

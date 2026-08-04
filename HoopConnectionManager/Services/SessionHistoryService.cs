using System.IO;
using System.Text.Json;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

public sealed class SessionHistoryService : ISessionHistoryService
{
    private const int MaximumEntries = 500;
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private List<SessionHistoryEntry> _entries;

    public event EventHandler? HistoryChanged;

    public SessionHistoryService(string? baseDirectory = null)
    {
        var root = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationConstants.StorageRootName,
            ApplicationConstants.DataDirectoryName);
        _filePath = Path.Combine(root, "session-history.json");
        _entries = Load();
        var interrupted = _entries.Where(entry => entry.EndedAt is null).ToList();
        foreach (var entry in interrupted)
        {
            entry.EndedAt = DateTime.Now;
            entry.EndReason = "Aplicativo encerrado inesperadamente";
        }
        if (interrupted.Count > 0) TrimAndSave();
    }

    public IReadOnlyList<SessionHistoryEntry> GetEntries()
    {
        lock (_lock)
            return _entries.OrderByDescending(entry => entry.StartedAt).Select(Clone).ToList();
    }

    public void StartSession(ActiveTunnel tunnel)
    {
        if (tunnel.Credentials is null) return;
        lock (_lock)
        {
            if (_entries.Any(entry => entry.Id == tunnel.Id)) return;
            _entries.Add(new SessionHistoryEntry
            {
                Id = tunnel.Id,
                ConnectionName = tunnel.ConnectionName,
                StartedAt = tunnel.StartedAt,
                Port = tunnel.Credentials.Port
            });
            TrimAndSave();
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EndSession(string tunnelId, string reason)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(item => item.Id == tunnelId);
            if (entry is null || entry.EndedAt is not null) return;
            entry.EndedAt = DateTime.Now;
            entry.EndReason = string.IsNullOrWhiteSpace(reason) ? "Sessão encerrada" : reason;
            TrimAndSave();
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private List<SessionHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return [];
            return JsonSerializer.Deserialize<List<SessionHistoryEntry>>(File.ReadAllText(_filePath), _jsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void TrimAndSave()
    {
        _entries = _entries.OrderByDescending(entry => entry.StartedAt).Take(MaximumEntries).ToList();
        try
        {
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_entries, _jsonOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O histórico nunca deve impedir a abertura ou o encerramento de um túnel.
        }
    }

    private static SessionHistoryEntry Clone(SessionHistoryEntry entry) => new()
    {
        Id = entry.Id,
        ConnectionName = entry.ConnectionName,
        StartedAt = entry.StartedAt,
        EndedAt = entry.EndedAt,
        Port = entry.Port,
        EndReason = entry.EndReason
    };
}

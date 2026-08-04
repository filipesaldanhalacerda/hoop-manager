using HoopConnectionManager.Models;

namespace HoopConnectionManager.Services.Abstractions;

public interface ISessionHistoryService
{
    event EventHandler? HistoryChanged;
    IReadOnlyList<SessionHistoryEntry> GetEntries();
    void StartSession(ActiveTunnel tunnel);
    void EndSession(string tunnelId, string reason);
}

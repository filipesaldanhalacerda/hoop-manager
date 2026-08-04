namespace HoopConnectionManager.Models;

public sealed class SessionHistoryEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ConnectionName { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; set; }
    public int Port { get; init; }
    public string EndReason { get; set; } = "Sessão ativa";

    public TimeSpan Duration => (EndedAt ?? DateTime.Now) - StartedAt;
    public string StartLabel => StartedAt.ToString("dd/MM/yyyy HH:mm:ss");
    public string EndLabel => EndedAt?.ToString("dd/MM/yyyy HH:mm:ss") ?? "Em andamento";
    public string DurationLabel
    {
        get
        {
            var duration = Duration < TimeSpan.Zero ? TimeSpan.Zero : Duration;
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes:00}min"
                : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}min {duration.Seconds:00}s" : $"{duration.Seconds}s";
        }
    }
    public string PortLabel => Port.ToString();
    public bool IsActive => EndedAt is null;
}

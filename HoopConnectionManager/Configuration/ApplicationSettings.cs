namespace HoopConnectionManager.Configuration;

/// <summary>
/// Configurações persistentes do aplicativo.
/// </summary>
public sealed class ApplicationSettings
{
    public string HoopExecutablePath { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool DisconnectTunnelsOnExit { get; set; } = true;
    public bool RefreshConnectionsOnStartup { get; set; } = true;
    public int LogRetentionDays { get; set; } = 14;
    public string Theme { get; set; } = "Auto";
    public bool IsFirstRunCompleted { get; set; }
    public List<string> FavoriteConnectionIds { get; set; } = [];
    public List<string> RecentConnectionIds { get; set; } = [];
}

namespace HoopConnectionManager.Models;

/// <summary>
/// Representa uma conexão disponível via Hoop CLI.
/// </summary>
public sealed class Connection
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string? FriendlyName { get; init; }
    public EnvironmentType Environment { get; init; } = EnvironmentType.Unknown;
    public string Type { get; init; } = string.Empty;
    public bool IsFavorite { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? Name : FriendlyName;

    public string EnvironmentGroup => Environment switch
    {
        EnvironmentType.Development => "DEV",
        EnvironmentType.Staging => "STG",
        EnvironmentType.Production => "PRD",
        _ => "UNKNOWN"
    };
}

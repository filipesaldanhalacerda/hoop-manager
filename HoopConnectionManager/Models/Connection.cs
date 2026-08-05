namespace HoopConnectionManager.Models;

/// <summary>
/// Representa uma conexão disponível via Hoop CLI.
/// </summary>
public sealed class Connection
{
    public string Id => Name;
    public string Name { get; init; } = string.Empty;
    public string? FriendlyName { get; init; }
    public EnvironmentType Environment { get; init; } = EnvironmentType.Unknown;
    public string Type { get; init; } = string.Empty;
    public bool IsFavorite { get; set; }
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Partes do nome no padrão corporativo, quando ele segue esse padrão.</summary>
    public HoopConnectionName? NameParts =>
        HoopConnectionName.TryParse(Name, out var parsed) ? parsed : null;

    /// <summary>
    /// Nome exibido. Prefere o apelido vindo do catálogo; sem ele, usa o rótulo curto
    /// derivado do padrão corporativo; e só então o nome completo.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName)
        ? NameParts?.ShortLabel ?? Name
        : FriendlyName;

    public string EnvironmentGroup => Environment switch
    {
        EnvironmentType.Development => "DEV",
        EnvironmentType.Staging => "STG",
        EnvironmentType.Production => "PRD",
        _ => "OUTROS"
    };
}

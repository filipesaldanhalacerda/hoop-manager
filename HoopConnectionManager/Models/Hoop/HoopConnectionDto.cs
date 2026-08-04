namespace HoopConnectionManager.Models.Hoop;

/// <summary>
/// Representação intermediária de uma conexão retornada pelo Hoop CLI.
/// Suporta múltiplos formatos de resposta.
/// </summary>
public sealed class HoopConnectionDto
{
    public string? Name { get; set; }
    public string? FriendlyName { get; set; }
    public string? Environment { get; set; }
    public string? Type { get; set; }
}

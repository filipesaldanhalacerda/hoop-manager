namespace HoopConnectionManager.Models;

/// <summary>
/// Non-sensitive information about the local Hoop CLI integration.
/// </summary>
public sealed class HoopDiagnostics
{
    public string Version { get; init; } = "Não identificada";
    public string GatewayUrl { get; init; } = "Não identificado";
    public string ConfigurationSource { get; init; } = "Arquivo local";
    public bool IsAuthenticated { get; init; }
    public bool SupportsVersionManager { get; init; }
}

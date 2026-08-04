namespace HoopConnectionManager.Models.Hoop;

/// <summary>
/// Representação intermediária das credenciais expostas pelo Hoop CLI durante um connect.
/// </summary>
public sealed class HoopCredentialsDto
{
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

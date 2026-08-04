namespace HoopConnectionManager.Models;

/// <summary>Registro seguro de uma ação operacional exibida no aplicativo.</summary>
public sealed record LogEntry(DateTime Timestamp, string Level, string Message)
{
    public string TimeLabel => Timestamp.ToString("HH:mm:ss");
    public string LevelLabel => Level switch
    {
        "ERROR" => "ERRO",
        "WARN" => "ALERTA",
        _ => "INFO"
    };
}

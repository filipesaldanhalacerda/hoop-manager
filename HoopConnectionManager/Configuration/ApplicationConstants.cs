namespace HoopConnectionManager.Configuration;

/// <summary>
/// Constantes globais do aplicativo.
/// </summary>
public static class ApplicationConstants
{
    public const string ApplicationName = "Dev Access Center";
    // Preserve the existing local profile so the rebrand does not reset settings or logs.
    public const string StorageRootName = "Hoop Connection Manager";
    public const string HoopExecutableName = "hoop.exe";
    public const string SettingsFileName = "settings.json";
    public const string LogsDirectoryName = "Logs";
    public const string DataDirectoryName = "Data";

    /// <summary>
    /// Banco usado para abrir a sessão. O Hoop não informa o database na saída do
    /// `connect` — só host, porta, usuário e senha — então usamos o banco de manutenção
    /// padrão do PostgreSQL, que serve de porta de entrada para navegar os demais.
    /// </summary>
    public const string DefaultDatabaseName = "postgres";
}

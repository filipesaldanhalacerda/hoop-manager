namespace HoopConnectionManager.Models;

/// <summary>
/// Partes de um nome de conexão do Hoop no padrão corporativo:
/// <c>tecnologia-ambiente-gerenciador-time-banco-acesso</c>, por exemplo
/// <c>techsaz-dev-postgres-corafoundation002-iboundbra-rw</c>.
/// </summary>
public sealed record HoopConnectionName(
    string Technology,
    string Environment,
    string Engine,
    string Team,
    string Database,
    string Access)
{
    private static readonly HashSet<string> KnownEnvironments = new(StringComparer.OrdinalIgnoreCase)
    {
        "dev", "development", "hml", "homolog", "stg", "staging", "qa", "uat", "sit", "prd", "prod", "production"
    };

    private static readonly HashSet<string> KnownAccessSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "rw", "ro", "r", "w"
    };

    /// <summary>
    /// Rótulo curto para exibição: <c>iboundbra-dev-rw</c>. O nome completo tem quase
    /// sessenta caracteres e não cabe nas listas sem truncar justamente a parte que
    /// identifica o banco, que fica no meio.
    /// </summary>
    public string ShortLabel => string.Join('-', new[] { Database, Environment, Access }
        .Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>
    /// Segmento que nomeia o banco. É o candidato natural para o campo Database do
    /// cliente, mas continua sendo uma dedução do nome — não algo que o Hoop informe.
    /// </summary>
    public string SuggestedDatabase => Database;

    /// <summary>
    /// Interpreta o nome no padrão corporativo. Devolve <c>false</c> para qualquer coisa
    /// fora do formato esperado, para não inventar significado onde não há: nesse caso
    /// quem chama continua usando o nome original.
    /// </summary>
    public static bool TryParse(string? connectionName, out HoopConnectionName parsed)
    {
        parsed = null!;

        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return false;
        }

        var parts = connectionName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 6 || !KnownEnvironments.Contains(parts[1]))
        {
            return false;
        }

        // O nome do banco pode conter hífen, então ancoramos pelas duas pontas: os quatro
        // primeiros segmentos são fixos e o último é o acesso, quando reconhecido.
        var hasAccess = KnownAccessSuffixes.Contains(parts[^1]);
        var access = hasAccess ? parts[^1] : string.Empty;
        var databaseEnd = hasAccess ? parts.Length - 1 : parts.Length;
        var database = string.Join('-', parts[4..databaseEnd]);

        if (string.IsNullOrEmpty(database))
        {
            return false;
        }

        parsed = new HoopConnectionName(parts[0], parts[1], parts[2], parts[3], database, access);
        return true;
    }
}

namespace HoopConnectionManager.Models;

/// <summary>
/// Retrato do que ainda falta para o ambiente ficar operacional.
/// Substitui a pergunta "o assistente já foi exibido?" por "o ambiente está pronto?",
/// que é o que realmente importa para quem acabou de entrar no time.
/// </summary>
/// <param name="HoopInstalled">O executável do Hoop CLI foi localizado.</param>
/// <param name="HoopAuthenticated">O Hoop confirmou uma sessão válida nesta máquina.</param>
/// <param name="DBeaverLocated">O executável do DBeaver foi localizado.</param>
public sealed record EnvironmentReadiness(
    bool HoopInstalled,
    bool HoopAuthenticated,
    bool DBeaverLocated)
{
    public bool IsReady => HoopInstalled && HoopAuthenticated && DBeaverLocated;

    /// <summary>
    /// Primeira etapa do assistente que ainda precisa de atenção. A etapa 3 (catálogo)
    /// não entra na conta: ela apenas lista conexões e nada persiste do resultado.
    /// </summary>
    public int FirstPendingStep =>
        !HoopInstalled ? 1
        : !HoopAuthenticated ? 2
        : !DBeaverLocated ? 4
        : 5;

    public IReadOnlyList<string> PendingItems
    {
        get
        {
            var pending = new List<string>();
            if (!HoopInstalled) pending.Add("Hoop CLI");
            if (!HoopAuthenticated) pending.Add("autenticação no Hoop");
            if (!DBeaverLocated) pending.Add("DBeaver");
            return pending;
        }
    }

    public string Summary => IsReady
        ? "Hoop e DBeaver localizados, sessão válida."
        : $"Ainda falta configurar: {string.Join(", ", PendingItems)}.";
}

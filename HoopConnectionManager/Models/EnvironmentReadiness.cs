namespace HoopConnectionManager.Models;

/// <summary>
/// Retrato do que ainda falta para o ambiente ficar operacional.
/// Substitui a pergunta "o assistente já foi exibido?" por "o ambiente está pronto?",
/// que é o que realmente importa para quem acabou de entrar no time.
/// </summary>
/// <param name="HoopInstalled">O executável do Hoop CLI foi localizado.</param>
/// <param name="HoopAuthenticated">O Hoop confirmou uma sessão válida nesta máquina.</param>
public sealed record EnvironmentReadiness(bool HoopInstalled, bool HoopAuthenticated)
{
    public bool IsReady => HoopInstalled && HoopAuthenticated;

    /// <summary>
    /// Primeira etapa do assistente que ainda precisa de atenção. A etapa 3 (catálogo)
    /// não entra na conta: ela apenas lista conexões e nada persiste do resultado.
    /// </summary>
    public int FirstPendingStep =>
        !HoopInstalled ? 1
        : !HoopAuthenticated ? 2
        : 4;

    public IReadOnlyList<string> PendingItems
    {
        get
        {
            var pending = new List<string>();
            if (!HoopInstalled) pending.Add("Hoop CLI");
            if (!HoopAuthenticated) pending.Add("autenticação no Hoop");
            return pending;
        }
    }

    public string Summary => IsReady
        ? "Hoop localizado e sessão válida."
        : $"Ainda falta configurar: {string.Join(", ", PendingItems)}.";
}

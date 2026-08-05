using HoopConnectionManager.Models;

namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Controla o assistente de configuração e responde se o ambiente está operacional.
/// </summary>
public interface IFirstRunService
{
    bool ShouldShowWizard();
    Task CompleteWizardAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica o estado real da máquina: Hoop localizado, sessão válida e DBeaver
    /// localizado. É o que mantém a configuração guiada acessível depois da primeira vez.
    /// </summary>
    Task<EnvironmentReadiness> EvaluateReadinessAsync(CancellationToken cancellationToken = default);
}

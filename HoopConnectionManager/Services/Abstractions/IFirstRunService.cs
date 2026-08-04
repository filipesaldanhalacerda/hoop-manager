namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Determina se o assistente de primeira execução deve ser exibido.
/// </summary>
public interface IFirstRunService
{
    bool ShouldShowWizard();
    Task CompleteWizardAsync(CancellationToken cancellationToken = default);
}

using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do serviço de primeira execução.
/// </summary>
public sealed class FirstRunService : IFirstRunService
{
    private readonly ISettingsService _settingsService;
    private readonly IHoopService _hoopService;

    public FirstRunService(ISettingsService settingsService, IHoopService hoopService)
    {
        _settingsService = settingsService;
        _hoopService = hoopService;
    }

    public bool ShouldShowWizard()
    {
        return !_settingsService.Load().IsFirstRunCompleted;
    }

    public async Task CompleteWizardAsync(CancellationToken cancellationToken = default)
    {
        await _settingsService.UpdateAsync(settings => settings.IsFirstRunCompleted = true, cancellationToken);
    }

    public async Task<EnvironmentReadiness> EvaluateReadinessAsync(CancellationToken cancellationToken = default)
    {
        var installed = await _hoopService.IsInstalledAsync(cancellationToken);
        var authenticated = installed && await _hoopService.IsAuthenticatedAsync(cancellationToken);
        return new EnvironmentReadiness(installed, authenticated);
    }
}

using HoopConnectionManager.Configuration;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do serviço de primeira execução.
/// </summary>
public sealed class FirstRunService : IFirstRunService
{
    private readonly ISettingsService _settingsService;

    public FirstRunService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool ShouldShowWizard()
    {
        return !_settingsService.Load().IsFirstRunCompleted;
    }

    public async Task CompleteWizardAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        settings.IsFirstRunCompleted = true;
        await _settingsService.SaveAsync(settings, cancellationToken);
    }
}

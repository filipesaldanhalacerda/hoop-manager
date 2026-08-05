using System.IO;
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
    private readonly IDBeaverService _dbeaverService;

    public FirstRunService(
        ISettingsService settingsService,
        IHoopService hoopService,
        IDBeaverService dbeaverService)
    {
        _settingsService = settingsService;
        _hoopService = hoopService;
        _dbeaverService = dbeaverService;
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
        return new EnvironmentReadiness(installed, authenticated, await IsDBeaverLocatedAsync(cancellationToken));
    }

    /// <summary>
    /// Confere primeiro o caminho já gravado: <see cref="IDBeaverService.LocateAsync"/>
    /// persiste o resultado, e esta verificação roda periodicamente — sondar sempre
    /// significaria gravar as configurações em disco a cada ciclo.
    /// </summary>
    private async Task<bool> IsDBeaverLocatedAsync(CancellationToken cancellationToken)
    {
        var configured = _settingsService.Load().DBeaverExecutablePath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return true;
        }

        return await _dbeaverService.LocateAsync(cancellationToken) is not null;
    }
}

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.ViewModels;

/// <summary>
/// ViewModel do assistente de primeira execução.
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    private readonly IHoopService _hoopService;
    private readonly IInstallerService _installerService;
    private readonly ILoginService _loginService;
    private readonly IDBeaverService _dbeaverService;
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notificationService;
    private readonly IFirstRunService _firstRunService;
    private readonly ILoggerService _logger;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Verificando instalação...";

    [ObservableProperty]
    private int _installerProgress;

    [ObservableProperty]
    private string _installerLog = string.Empty;

    [ObservableProperty]
    private string _installerScriptPath = string.Empty;

    [ObservableProperty]
    private string? _dbeaverPath;

    [ObservableProperty]
    private bool _connectionsLoaded;

    public IReadOnlyList<string> DiscoveredConnectionGroups { get; } = [];

    public WizardViewModel(
        IHoopService hoopService,
        IInstallerService installerService,
        ILoginService loginService,
        IDBeaverService dbeaverService,
        INavigationService navigationService,
        INotificationService notificationService,
        IFirstRunService firstRunService,
        ILoggerService logger)
    {
        _hoopService = hoopService;
        _installerService = installerService;
        _loginService = loginService;
        _dbeaverService = dbeaverService;
        _navigationService = navigationService;
        _notificationService = notificationService;
        _firstRunService = firstRunService;
        _logger = logger;

        _installerService.ProgressChanged += (_, e) =>
        {
            InstallerProgress = e.PercentComplete;
            InstallerLog += $"{e.Message}{Environment.NewLine}";
        };
    }

    [RelayCommand]
    private async Task CheckInstallationAsync()
    {
        IsBusy = true;
        StatusMessage = "Verificando Hoop...";

        var installed = await _hoopService.IsInstalledAsync();
        if (installed)
        {
            CurrentStep = 2;
        }

        IsBusy = false;
    }

    [RelayCommand]
    private async Task InstallHoopAsync()
    {
        if (string.IsNullOrWhiteSpace(InstallerScriptPath) || !File.Exists(InstallerScriptPath))
        {
            _notificationService.Show("Selecione o script oficial de instalação do Hoop.", NotificationLevel.Warning);
            return;
        }

        IsBusy = true;
        InstallerProgress = 0;
        InstallerLog = string.Empty;

        try
        {
            var installed = await _installerService.InstallAsync(InstallerScriptPath);
            if (installed)
            {
                CurrentStep = 2;
            }
            else
            {
                _notificationService.Show("A instalação foi concluída, mas o Hoop não foi localizado.", NotificationLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show($"Erro na instalação: {ex.Message}", NotificationLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsBusy = true;
        StatusMessage = "Aguardando login no Hoop...";

        try
        {
            var success = await _loginService.LoginAsync();
            if (success)
            {
                _notificationService.Show("Login realizado com sucesso.");
                CurrentStep = 3;
            }
            else
            {
                _notificationService.Show("Não foi possível confirmar o login.", NotificationLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show($"Erro no login: {ex.Message}", NotificationLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckDBeaverAsync()
    {
        DbeaverPath = await _dbeaverService.LocateAsync();
        if (DbeaverPath is not null)
        {
            CurrentStep = 5;
        }
        else
        {
            _notificationService.Show("DBeaver não encontrado. Selecione o caminho manualmente na próxima etapa.", NotificationLevel.Warning);
        }
    }

    [RelayCommand]
    private async Task LoadConnectionsAsync()
    {
        IsBusy = true;
        StatusMessage = "Buscando conexões...";

        try
        {
            var connections = await _hoopService.GetConnectionsAsync();
            ConnectionsLoaded = true;
            _logger.LogInformation($"{connections.Count} conexões descobertas no wizard.");

            var groups = connections
                .GroupBy(c => c.EnvironmentGroup)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();

            _notificationService.Show($"Conexões encontradas: {string.Join(", ", groups)}");
            CurrentStep = 4;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao buscar conexões no wizard.");
            _notificationService.Show($"Erro ao buscar conexões: {ex.Message}", NotificationLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        await _firstRunService.CompleteWizardAsync();
        _navigationService.NavigateTo<DashboardViewModel>();
    }
}

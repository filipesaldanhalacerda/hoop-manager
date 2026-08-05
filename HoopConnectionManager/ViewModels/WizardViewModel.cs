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
    private readonly ILoginService _loginService;
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notificationService;
    private readonly IFirstRunService _firstRunService;
    private readonly ILoggerService _logger;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Clique em Verificar Instalação para localizar o Hoop.";

    [ObservableProperty]
    private bool _connectionsLoaded;

    public WizardViewModel(
        IHoopService hoopService,
        ILoginService loginService,
        INavigationService navigationService,
        INotificationService notificationService,
        IFirstRunService firstRunService,
        ILoggerService logger)
    {
        _hoopService = hoopService;
        _loginService = loginService;
        _navigationService = navigationService;
        _notificationService = notificationService;
        _firstRunService = firstRunService;
        _logger = logger;
    }

    /// <summary>
    /// Reposiciona o assistente na primeira etapa que ainda precisa de atenção.
    /// Precisa ser chamado sempre que a tela é aberta: o ViewModel é singleton e
    /// guardaria o passo da sessão anterior, que pode não refletir a máquina de agora.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "Verificando o que já está configurado...";

        try
        {
            var readiness = await _firstRunService.EvaluateReadinessAsync(cancellationToken);
            CurrentStep = readiness.FirstPendingStep;
            StatusMessage = readiness.IsReady
                ? "Ambiente pronto. Revise as etapas ou volte para a central de conexões."
                : readiness.Summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao avaliar o ambiente ao abrir o assistente.");
            CurrentStep = 1;
            StatusMessage = "Não foi possível verificar o ambiente. Comece pela primeira etapa.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckInstallationAsync()
    {
        IsBusy = true;
        StatusMessage = "Verificando Hoop...";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            var installed = await _hoopService.IsInstalledAsync(timeout.Token);
            if (installed)
            {
                StatusMessage = "Hoop encontrado. Continue para realizar o login.";
                CurrentStep = 2;
                return;
            }

            StatusMessage = "Hoop não encontrado. Confirme a instalação e tente novamente.";
            _notificationService.Show("Hoop não encontrado neste computador.", NotificationLevel.Warning);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            StatusMessage = "A verificação demorou demais. Tente novamente ou selecione o instalador.";
            _notificationService.Show("A verificação do Hoop excedeu o limite de 15 segundos.", NotificationLevel.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao verificar a instalação do Hoop.");
            StatusMessage = "Não foi possível verificar a instalação. Tente novamente.";
            _notificationService.Show($"Erro ao verificar o Hoop: {ex.Message}", NotificationLevel.Error);
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
            CurrentStep = 4; // Concluído
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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoopConnectionManager.Services.Abstractions;
using HoopConnectionManager.Models;
using System.Windows.Threading;

namespace HoopConnectionManager.ViewModels;

/// <summary>
/// ViewModel da janela principal.
/// Gerencia a navegação entre telas e ações globais.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IHoopService _hoopService;
    private readonly ILoginService _loginService;
    private readonly DispatcherTimer _connectivityTimer;
    private readonly HashSet<string> _recoveringConnections = new(StringComparer.OrdinalIgnoreCase);
    private bool _connectivityCheckInProgress;
    private CancellationTokenSource? _notificationCancellation;

    [ObservableProperty]
    private string _title = "Dev Access Center";

    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private string _statusMessage = "Pronto";

    [ObservableProperty]
    private string _notificationMessage = string.Empty;

    [ObservableProperty]
    private string _notificationKind = "Information";

    [ObservableProperty]
    private string _notificationGlyph = "\uE946";

    [ObservableProperty]
    private bool _isNotificationVisible;

    [ObservableProperty] private string _notificationActionLabel = string.Empty;
    [ObservableProperty] private bool _hasNotificationAction;
    private NotificationAction _notificationAction;

    [ObservableProperty]
    private bool _isSecurityDetailsVisible;

    [ObservableProperty] private string _connectivityLabel = "Verificando...";
    [ObservableProperty] private string _connectivityDetail = "Verificando conectividade do ambiente.";
    [ObservableProperty] private string _connectivityKind = "Checking";

    public MainWindowViewModel(
        INavigationService navigationService,
        ILoggerService logger,
        INotificationService notificationService,
        IHoopService hoopService,
        IConnectionService connectionService,
        ILoginService loginService)
    {
        _navigationService = navigationService;
        _hoopService = hoopService;
        _loginService = loginService;
        _navigationService.Navigated += (_, e) => CurrentViewModel = e.ViewModel;
        CurrentViewModel = _navigationService.CurrentViewModel;
        notificationService.NotificationRaised += (_, e) => ShowNotification(e);
        connectionService.ActiveTunnelsChanged += (_, e) =>
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Status == ConnectionStatus.Reconnecting)
                {
                    _recoveringConnections.Add(e.ConnectionName);
                }
                else
                {
                    _recoveringConnections.Remove(e.ConnectionName);
                }

                if (_recoveringConnections.Count > 0)
                {
                    ShowRecoveringState();
                }
                else
                {
                    _ = UpdateConnectivityAsync();
                }
            }));
        };

        _connectivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _connectivityTimer.Tick += async (_, _) => await UpdateConnectivityAsync();
        _connectivityTimer.Start();
        _ = UpdateConnectivityAsync();

        logger.LogInformation("MainWindowViewModel inicializado.");
    }

    private async Task UpdateConnectivityAsync()
    {
        if (_connectivityCheckInProgress || _recoveringConnections.Count > 0)
        {
            return;
        }

        _connectivityCheckInProgress = true;
        try
        {
            var connectivity = await _hoopService.GetConnectivityAsync();
            ConnectivityKind = connectivity.State.ToString();
            ConnectivityLabel = connectivity.State switch
            {
                GlobalConnectivityState.Online => "Online",
                GlobalConnectivityState.NoNetwork => "Sem rede",
                GlobalConnectivityState.AuthenticationExpired => "Autenticação expirada",
                GlobalConnectivityState.GatewayUnavailable => "Gateway indisponível",
                _ => "Hoop desconectado"
            };
            ConnectivityDetail = connectivity.Detail;
        }
        catch (Exception ex)
        {
            ConnectivityKind = nameof(GlobalConnectivityState.HoopDisconnected);
            ConnectivityLabel = "Hoop desconectado";
            ConnectivityDetail = $"Não foi possível verificar a conectividade: {ex.Message}";
        }
        finally
        {
            _connectivityCheckInProgress = false;
        }
    }

    private void ShowRecoveringState()
    {
        ConnectivityKind = "Recovering";
        ConnectivityLabel = "Recuperando conexões";
        ConnectivityDetail = _recoveringConnections.Count == 1
            ? "Uma conexão está em recuperação progressiva."
            : $"{_recoveringConnections.Count} conexões estão em recuperação progressiva.";
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _navigationService.NavigateTo<SettingsViewModel>();
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        _navigationService.NavigateTo<DashboardViewModel>();
    }

    [RelayCommand]
    private void NavigateToLogs()
    {
        _navigationService.NavigateTo<LogsViewModel>();
    }

    [RelayCommand]
    private void NavigateToSessionHistory()
    {
        _navigationService.NavigateTo<SessionHistoryViewModel>();
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.GoBack();
    }

    [RelayCommand]
    private void DismissNotification()
    {
        _notificationCancellation?.Cancel();
        IsNotificationVisible = false;
    }

    [RelayCommand]
    private async Task ExecuteNotificationActionAsync()
    {
        var action = _notificationAction;
        DismissNotification();
        switch (action)
        {
            case NotificationAction.Reauthenticate:
                if (!await _loginService.LoginAsync())
                {
                    ConnectivityKind = nameof(GlobalConnectivityState.AuthenticationExpired);
                    ConnectivityLabel = "Autenticação expirada";
                    ConnectivityDetail = "O navegador não confirmou uma nova sessão.";
                }
                else
                {
                    await UpdateConnectivityAsync();
                }
                break;
            case NotificationAction.OpenSettings:
                _navigationService.NavigateTo<SettingsViewModel>();
                break;
            case NotificationAction.SelectDBeaver:
                _navigationService.NavigateTo<SettingsViewModel>();
                if (_navigationService.CurrentViewModel is SettingsViewModel settings)
                {
                    settings.BrowseDBeaverExecutableCommand.Execute(null);
                }
                break;
        }
    }

    [RelayCommand]
    private void ShowSecurityDetails() => IsSecurityDetailsVisible = true;

    [RelayCommand]
    private void HideSecurityDetails() => IsSecurityDetailsVisible = false;

    private void ShowNotification(NotificationEventArgs notification)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            NotificationMessage = notification.Message;
            NotificationKind = notification.Level.ToString();
            NotificationGlyph = notification.Level switch
            {
                NotificationLevel.Error => "\uEA39",
                NotificationLevel.Warning => "\uE7BA",
                _ => "\uE946"
            };
            _notificationAction = notification.Action;
            NotificationActionLabel = notification.Action switch
            {
                NotificationAction.Reauthenticate => "Autenticar novamente",
                NotificationAction.SelectDBeaver => "Selecionar DBeaver",
                NotificationAction.OpenSettings => "Abrir configurações",
                _ => string.Empty
            };
            HasNotificationAction = notification.Action != NotificationAction.None;
            IsNotificationVisible = true;

            var cancellation = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _notificationCancellation, cancellation);
            previous?.Cancel();
            previous?.Dispose();
            if (notification.Action == NotificationAction.None)
            {
                _ = HideNotificationLaterAsync(notification.Level, cancellation.Token);
            }
        });
    }

    private async Task HideNotificationLaterAsync(NotificationLevel level, CancellationToken cancellationToken)
    {
        try
        {
            var duration = level == NotificationLevel.Error ? TimeSpan.FromSeconds(9) : TimeSpan.FromSeconds(5);
            await Task.Delay(duration, cancellationToken);
            IsNotificationVisible = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}

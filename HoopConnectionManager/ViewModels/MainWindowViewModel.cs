using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.ViewModels;

/// <summary>
/// ViewModel da janela principal.
/// Gerencia a navegação entre telas e ações globais.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
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

    [ObservableProperty]
    private bool _isSecurityDetailsVisible;

    public MainWindowViewModel(INavigationService navigationService, ILoggerService logger, INotificationService notificationService)
    {
        _navigationService = navigationService;
        _navigationService.Navigated += (_, e) => CurrentViewModel = e.ViewModel;
        CurrentViewModel = _navigationService.CurrentViewModel;
        notificationService.NotificationRaised += (_, e) => ShowNotification(e);

        logger.LogInformation("MainWindowViewModel inicializado.");
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
            IsNotificationVisible = true;

            var cancellation = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _notificationCancellation, cancellation);
            previous?.Cancel();
            previous?.Dispose();
            _ = HideNotificationLaterAsync(notification.Level, cancellation.Token);
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

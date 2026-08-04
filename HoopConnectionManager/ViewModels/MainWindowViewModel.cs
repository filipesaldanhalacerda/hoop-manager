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

    [ObservableProperty]
    private string _title = "Hoop Connection Manager";

    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private string _statusMessage = "Pronto";

    public MainWindowViewModel(INavigationService navigationService, ILoggerService logger)
    {
        _navigationService = navigationService;
        _navigationService.Navigated += (_, e) => CurrentViewModel = e.ViewModel;
        CurrentViewModel = _navigationService.CurrentViewModel;

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
}

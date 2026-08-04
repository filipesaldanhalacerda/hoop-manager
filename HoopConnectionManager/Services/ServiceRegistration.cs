using HoopConnectionManager.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using HoopConnectionManager.ViewModels;
using HoopConnectionManager.Views;

namespace HoopConnectionManager.Services;

/// <summary>
/// Extensão para registrar todos os serviços da aplicação no container de DI.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ILoggerService, LoggerService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ICommandRunner, CommandRunner>();
        services.AddSingleton<IHoopService, HoopService>();
        services.AddSingleton<IInstallerService, InstallerService>();
        services.AddSingleton<ILoginService, LoginService>();
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IDBeaverService, DBeaverService>();
        services.AddSingleton<IFirstRunService, FirstRunService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<INavigationService>(provider => new NavigationService(provider));
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<WizardViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<LogsViewModel>();

        return services;
    }
}

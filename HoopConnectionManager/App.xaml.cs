using System.Windows;
using HoopConnectionManager.Helpers;
using HoopConnectionManager.Services;
using HoopConnectionManager.Services.Abstractions;
using HoopConnectionManager.ViewModels;
using HoopConnectionManager.Views;
using Microsoft.Extensions.DependencyInjection;

namespace HoopConnectionManager;

/// <summary>
/// Ponto de entrada da aplicação WPF.
/// Configura o container de injeção de dependências e inicializa a janela principal.
/// </summary>
public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        Services = serviceCollection.BuildServiceProvider();

        ApplyTheme();
        InitializeTrayIcon();

        var navigationService = Services.GetRequiredService<INavigationService>();
        var firstRunService = Services.GetRequiredService<IFirstRunService>();

        if (firstRunService.ShouldShowWizard())
        {
            navigationService.NavigateTo<WizardViewModel>();
        }
        else
        {
            navigationService.NavigateTo<DashboardViewModel>();
        }

        var mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainWindowViewModel>()
        };

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var trayIcon = Services.GetService<ITrayIconService>();
        (trayIcon as IDisposable)?.Dispose();

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddApplicationServices();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<WizardViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    private static void ApplyTheme()
    {
        var settingsService = Services.GetRequiredService<ISettingsService>();
        var settings = settingsService.Load();
        ThemeManager.ApplyTheme(settings.Theme);
    }

    private static void InitializeTrayIcon()
    {
        var trayIcon = Services.GetRequiredService<ITrayIconService>();
        trayIcon.Initialize();

        trayIcon.OpenRequested += (_, _) =>
        {
            Current.Dispatcher.Invoke(() =>
            {
                Current.MainWindow ??= Services.GetRequiredService<MainWindow>();
                Current.MainWindow.Show();
                Current.MainWindow.WindowState = WindowState.Normal;
                Current.MainWindow.Activate();
            });
        };

        trayIcon.ExitRequested += (_, _) => Current.Shutdown();
    }
}

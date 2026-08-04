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
    private bool _shutdownInProgress;

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

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();
        mainWindow.ExitRequested += OnExitRequested;

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services.GetService<IConnectionService>() as IDisposable)?.Dispose();
        var trayIcon = Services.GetService<ITrayIconService>();
        (trayIcon as IDisposable)?.Dispose();

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddApplicationServices();

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

        trayIcon.ExitRequested += ((App)Current).OnExitRequested;
    }

    private async void OnExitRequested(object? sender, EventArgs e)
    {
        if (_shutdownInProgress) return;

        var connectionService = Services.GetRequiredService<IConnectionService>();
        var settings = Services.GetRequiredService<ISettingsService>().Load();
        var activeCount = connectionService.ActiveTunnels.Count;

        if (activeCount > 0 && !settings.DisconnectTunnelsOnExit)
        {
            var answer = System.Windows.MessageBox.Show(
                $"Existem {activeCount} {(activeCount == 1 ? "túnel ativo" : "túneis ativos")}. " +
                "Eles precisam ser encerrados para evitar processos Hoop órfãos. Deseja encerrar e sair?",
                "Encerrar conexões",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
        }

        _shutdownInProgress = true;
        try
        {
            if (activeCount > 0)
            {
                Services.GetRequiredService<ITrayIconService>().ShowBalloonTip(
                    Configuration.ApplicationConstants.ApplicationName,
                    "Encerrando conexões com segurança...");
                await connectionService.DisconnectAllAsync();
            }
        }
        catch (Exception ex)
        {
            Services.GetRequiredService<ILoggerService>().LogError(ex,
                "Uma ou mais conexões não puderam ser encerradas normalmente; os processos restantes serão finalizados na saída.");
        }
        finally
        {
            Services.GetRequiredService<MainWindow>().AuthorizeShutdown();
            Shutdown();
        }
    }
}

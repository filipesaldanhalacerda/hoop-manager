using System.Windows;
using HoopConnectionManager.Services.Abstractions;
using HoopConnectionManager.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HoopConnectionManager.Views;

/// <summary>
/// Janela principal da aplicação.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ITrayIconService _trayIconService;
    private readonly ISettingsService _settingsService;
    private bool _shutdownAuthorized;

    public event EventHandler? ExitRequested;

    public MainWindow()
    {
        InitializeComponent();

        _trayIconService = App.Services.GetRequiredService<ITrayIconService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownAuthorized)
        {
            return;
        }

        if (_settingsService.Load().MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIconService.ShowBalloonTip(ApplicationConstants.ApplicationName, "Aplicativo minimizado para a bandeja.");
            return;
        }

        e.Cancel = true;
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void AuthorizeShutdown() => _shutdownAuthorized = true;
}

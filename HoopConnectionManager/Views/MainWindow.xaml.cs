using System.Windows;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HoopConnectionManager.Views;

/// <summary>
/// Janela principal da aplicação.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ITrayIconService _trayIconService;
    private readonly ISettingsService _settingsService;

    public MainWindow()
    {
        InitializeComponent();

        _trayIconService = App.Services.GetRequiredService<ITrayIconService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_settingsService.Load().MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIconService.ShowBalloonTip("Hoop Connection Manager", "Aplicativo minimizado para a bandeja.");
        }
    }
}

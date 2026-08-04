using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoopConnectionManager.Helpers;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.Win32;

namespace HoopConnectionManager.ViewModels;

/// <summary>
/// ViewModel da tela de configurações.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupService _startupService;
    private readonly INotificationService _notificationService;

    [ObservableProperty]
    private string _hoopExecutablePath = string.Empty;

    [ObservableProperty]
    private string _dbeaverExecutablePath = string.Empty;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _openDBeaverAutomatically = true;

    [ObservableProperty]
    private bool _refreshConnectionsOnStartup = true;

    [ObservableProperty]
    private string _selectedTheme = "Auto";

    public IReadOnlyList<string> Themes { get; } = ["Auto", "Light", "Dark"];

    public SettingsViewModel(
        ISettingsService settingsService,
        IStartupService startupService,
        INotificationService notificationService)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _notificationService = notificationService;
        LoadSettings();
    }

    [RelayCommand]
    private void BrowseHoopExecutable()
    {
        var path = OpenFileDialog("Executáveis|*.exe|Todos os arquivos|*.*");
        if (!string.IsNullOrWhiteSpace(path))
        {
            HoopExecutablePath = path;
        }
    }

    [RelayCommand]
    private void BrowseDBeaverExecutable()
    {
        var path = OpenFileDialog("Executáveis|*.exe|Todos os arquivos|*.*");
        if (!string.IsNullOrWhiteSpace(path))
        {
            DbeaverExecutablePath = path;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = _settingsService.Load();
        settings.HoopExecutablePath = HoopExecutablePath;
        settings.DBeaverExecutablePath = DbeaverExecutablePath;
        settings.StartWithWindows = StartWithWindows;
        settings.MinimizeToTray = MinimizeToTray;
        settings.OpenDBeaverAutomatically = OpenDBeaverAutomatically;
        settings.RefreshConnectionsOnStartup = RefreshConnectionsOnStartup;
        settings.Theme = SelectedTheme;

        await _settingsService.SaveAsync(settings);

        ApplyStartupSetting();
        ThemeManager.ApplyTheme(SelectedTheme);

        _notificationService.Show("Configurações salvas com sucesso.");
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        HoopExecutablePath = settings.HoopExecutablePath;
        DbeaverExecutablePath = settings.DBeaverExecutablePath;
        StartWithWindows = settings.StartWithWindows;
        MinimizeToTray = settings.MinimizeToTray;
        OpenDBeaverAutomatically = settings.OpenDBeaverAutomatically;
        RefreshConnectionsOnStartup = settings.RefreshConnectionsOnStartup;
        SelectedTheme = settings.Theme;
    }

    private void ApplyStartupSetting()
    {
        try
        {
            if (StartWithWindows)
            {
                _startupService.EnableStartup();
            }
            else
            {
                _startupService.DisableStartup();
            }
        }
        catch (Exception ex)
        {
            _notificationService.Show($"Erro ao configurar inicialização com Windows: {ex.Message}", NotificationLevel.Error);
        }
    }

    private static string? OpenFileDialog(string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

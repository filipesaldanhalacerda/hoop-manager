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
    private readonly ILoggerService _logger;
    private readonly IHoopService _hoopService;
    private readonly ILoginService _loginService;
    private CancellationTokenSource? _themeSaveCancellation;

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

    [ObservableProperty]
    private string _saveStatus = "Alterações são aplicadas ao salvar.";

    [ObservableProperty] private string _hoopVersion = "Verificando...";
    [ObservableProperty] private string _hoopGateway = "Verificando...";
    [ObservableProperty] private string _hoopConfigurationSource = "Verificando...";
    [ObservableProperty] private string _hoopAuthenticationStatus = "Verificando...";
    [ObservableProperty] private string _hoopCompatibilityStatus = "Verificando...";
    [ObservableProperty] private bool _isHoopDiagnosticsBusy;

    public IReadOnlyList<string> Themes { get; } = ["Auto", "Light", "Dark"];

    public SettingsViewModel(
        ISettingsService settingsService,
        IStartupService startupService,
        INotificationService notificationService,
        ILoggerService logger,
        IHoopService hoopService,
        ILoginService loginService)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _notificationService = notificationService;
        _logger = logger;
        _hoopService = hoopService;
        _loginService = loginService;
        LoadSettings();
        _ = RefreshHoopDiagnosticsAsync();
    }

    [RelayCommand]
    private async Task RefreshHoopDiagnosticsAsync()
    {
        if (IsHoopDiagnosticsBusy)
        {
            return;
        }

        IsHoopDiagnosticsBusy = true;
        try
        {
            var diagnostics = await _hoopService.GetDiagnosticsAsync();
            HoopVersion = diagnostics.Version;
            HoopGateway = diagnostics.GatewayUrl;
            HoopConfigurationSource = diagnostics.ConfigurationSource;
            HoopAuthenticationStatus = diagnostics.IsAuthenticated ? "Autenticado" : "Autenticação necessária";
            HoopCompatibilityStatus = diagnostics.SupportsVersionManager
                ? "Compatível com sincronização de versão pelo gateway"
                : "CLI anterior à 1.74: atualização deve seguir a central corporativa";
        }
        catch (Exception ex)
        {
            HoopAuthenticationStatus = "Não foi possível verificar";
            HoopCompatibilityStatus = ex.Message;
            _logger.LogError(ex, "Falha ao obter diagnóstico seguro do Hoop.");
        }
        finally
        {
            IsHoopDiagnosticsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReauthenticateHoopAsync()
    {
        if (IsHoopDiagnosticsBusy)
        {
            return;
        }

        IsHoopDiagnosticsBusy = true;
        try
        {
            var success = await _loginService.LoginAsync();
            _notificationService.Show(
                success ? "Autenticação do Hoop renovada." : "O Hoop não confirmou a autenticação.",
                success ? NotificationLevel.Information : NotificationLevel.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao renovar autenticação do Hoop.");
            _notificationService.Show($"Falha ao autenticar: {ex.Message}", NotificationLevel.Error);
        }
        finally
        {
            IsHoopDiagnosticsBusy = false;
        }

        await RefreshHoopDiagnosticsAsync();
    }

    partial void OnSelectedThemeChanged(string value)
    {
        ThemeManager.ApplyTheme(value);

        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _themeSaveCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        _ = PersistThemeAsync(value, cancellation.Token);
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
        var validationError = ValidateExecutablePaths();
        if (validationError is not null)
        {
            SaveStatus = validationError;
            _notificationService.Show(validationError, NotificationLevel.Warning);
            return;
        }

        SaveStatus = "Salvando e aplicando configurações...";
        bool? previousStartupState = null;
        try
        {
            previousStartupState = _startupService.IsStartupEnabled();
            ApplyStartupSetting();
            await _settingsService.UpdateAsync(settings =>
            {
                settings.HoopExecutablePath = HoopExecutablePath.Trim();
                settings.DBeaverExecutablePath = DbeaverExecutablePath.Trim();
                settings.StartWithWindows = StartWithWindows;
                settings.MinimizeToTray = MinimizeToTray;
                settings.OpenDBeaverAutomatically = OpenDBeaverAutomatically;
                settings.RefreshConnectionsOnStartup = RefreshConnectionsOnStartup;
                settings.Theme = SelectedTheme;
            });
            ThemeManager.ApplyTheme(SelectedTheme);
            SaveStatus = $"Tudo aplicado às {DateTime.Now:HH:mm:ss}.";
            _logger.LogInformation("Configurações validadas e aplicadas.");
            _notificationService.Show("Configurações aplicadas com sucesso.");
        }
        catch (Exception ex)
        {
            if (previousStartupState is not null && previousStartupState != StartWithWindows)
            {
                try
                {
                    if (previousStartupState.Value)
                    {
                        _startupService.EnableStartup();
                    }
                    else
                    {
                        _startupService.DisableStartup();
                    }
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException, "Falha ao restaurar configuração de inicialização do Windows.");
                }
            }

            SaveStatus = "Não foi possível aplicar todas as configurações.";
            _logger.LogError(ex, "Falha ao aplicar configurações.");
            _notificationService.Show($"Erro ao salvar configurações: {ex.Message}", NotificationLevel.Error);
        }
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        HoopExecutablePath = settings.HoopExecutablePath;
        DbeaverExecutablePath = settings.DBeaverExecutablePath;
        try
        {
            StartWithWindows = _startupService.IsStartupEnabled();
        }
        catch
        {
            StartWithWindows = settings.StartWithWindows;
        }
        MinimizeToTray = settings.MinimizeToTray;
        OpenDBeaverAutomatically = settings.OpenDBeaverAutomatically;
        RefreshConnectionsOnStartup = settings.RefreshConnectionsOnStartup;
        SelectedTheme = settings.Theme;
    }

    private async Task PersistThemeAsync(string theme, CancellationToken cancellationToken)
    {
        try
        {
            // Evita várias gravações quando o usuário alterna rapidamente entre temas.
            await Task.Delay(150, cancellationToken);
            await _settingsService.UpdateAsync(settings => settings.Theme = theme, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Uma escolha mais recente substituiu esta.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao persistir o tema selecionado.");
            _notificationService.Show($"O tema foi aplicado, mas não pôde ser salvo: {ex.Message}", NotificationLevel.Warning);
        }
    }

    private void ApplyStartupSetting()
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

    private string? ValidateExecutablePaths()
    {
        if (!string.IsNullOrWhiteSpace(HoopExecutablePath) && !File.Exists(HoopExecutablePath.Trim()))
        {
            return "O caminho configurado para o Hoop não existe.";
        }

        if (!string.IsNullOrWhiteSpace(DbeaverExecutablePath) && !File.Exists(DbeaverExecutablePath.Trim()))
        {
            return "O caminho configurado para o DBeaver não existe.";
        }

        if (!string.IsNullOrWhiteSpace(HoopExecutablePath)
            && !string.Equals(Path.GetExtension(HoopExecutablePath.Trim()), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Selecione um executável (.exe) válido para o Hoop.";
        }

        if (!string.IsNullOrWhiteSpace(DbeaverExecutablePath)
            && !string.Equals(Path.GetExtension(DbeaverExecutablePath.Trim()), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Selecione um executável (.exe) válido para o DBeaver.";
        }

        return null;
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

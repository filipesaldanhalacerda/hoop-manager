using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.ViewModels;

public sealed partial class LogsViewModel : ObservableObject
{
    private readonly ILoggerService _logger;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private DateTime _lastStorageRefresh = DateTime.MinValue;
    private bool _loadingRetention;

    public ObservableCollection<LogEntry> Entries { get; } = new();
    public ICollectionView FilteredEntries { get; }
    public IReadOnlyList<string> Levels { get; } = ["Todos", "INFO", "WARN", "ERROR"];
    public IReadOnlyList<int> RetentionOptions { get; } = [7, 14, 30, 60, 90];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedLevel = "Todos";
    [ObservableProperty] private string _summary = "Nenhum evento";
    [ObservableProperty] private bool _followLatest = true;
    [ObservableProperty] private int _selectedRetentionDays = 14;
    [ObservableProperty] private string _storageUsed = "0 KB";
    [ObservableProperty] private string _logFileCount = "Nenhum arquivo";
    [ObservableProperty] private string _oldestLogDate = "Sem registros";

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public int InfoCount => Entries.Count(entry => entry.Level == "INFO");
    public int WarningCount => Entries.Count(entry => entry.Level == "WARN");
    public int ErrorCount => Entries.Count(entry => entry.Level == "ERROR");

    public string LogsDirectory => _logger.LogsDirectory;

    public LogsViewModel(ILoggerService logger, ISettingsService settingsService, INotificationService notificationService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _loadingRetention = true;
        SelectedRetentionDays = NormalizeRetention(settingsService.Load().LogRetentionDays);
        _loadingRetention = false;
        foreach (var entry in logger.GetRecentEntries())
        {
            Entries.Add(entry);
        }

        FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = FilterEntry;
        _logger.LogWritten += OnLogWritten;
        UpdateSummary();
        RefreshStorageInfo();
    }

    partial void OnSelectedRetentionDaysChanged(int value)
    {
        if (!_loadingRetention) _ = SaveRetentionAsync(value);
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        RefreshFilter();
    }
    partial void OnSelectedLevelChanged(string value) => RefreshFilter();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void SelectLevel(string? level) => SelectedLevel = level ?? "Todos";

    [RelayCommand]
    private void OpenLogsFolder()
    {
        Process.Start(new ProcessStartInfo { FileName = LogsDirectory, UseShellExecute = true });
        _logger.LogInformation("Pasta de logs aberta pelo usuário.");
    }

    [RelayCommand]
    private void ClearOldLogs()
    {
        var removed = _logger.ClearOldLogs();
        _logger.LogInformation($"Limpeza manual de logs concluída: {removed} arquivo(s) antigo(s) removido(s).");
        RefreshStorageInfo();
        _notificationService.Show(removed == 0
            ? "Não havia arquivos antigos para remover."
            : $"{removed} arquivo(s) antigo(s) removido(s).");
    }

    private void OnLogWritten(object? sender, LogEntry entry)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > 500)
            {
                Entries.RemoveAt(0);
            }
            OnPropertyChanged(nameof(InfoCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(ErrorCount));
            UpdateSummary();
            if (DateTime.Now - _lastStorageRefresh >= TimeSpan.FromSeconds(30)) RefreshStorageInfo();
        });
    }

    private bool FilterEntry(object item)
    {
        if (item is not LogEntry entry)
        {
            return false;
        }

        var levelMatches = SelectedLevel == "Todos" || entry.Level == SelectedLevel;
        var searchMatches = string.IsNullOrWhiteSpace(SearchText)
            || entry.Message.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)
            || entry.LevelLabel.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
        return levelMatches && searchMatches;
    }

    private void RefreshFilter()
    {
        FilteredEntries.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var visible = FilteredEntries?.Cast<object>().Count() ?? Entries.Count;
        Summary = visible == 1 ? "1 evento exibido" : $"{visible} eventos exibidos";
    }

    private async Task SaveRetentionAsync(int days)
    {
        try
        {
            days = NormalizeRetention(days);
            await _settingsService.UpdateAsync(settings => settings.LogRetentionDays = days);
            _logger.ApplyRetention();
            RefreshStorageInfo();
            _logger.LogInformation($"Retenção de logs alterada para {days} dias.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao atualizar a retenção de logs.");
            _notificationService.Show("Não foi possível salvar o período de retenção.", NotificationLevel.Error);
        }
    }

    private void RefreshStorageInfo()
    {
        var info = _logger.GetStorageInfo();
        StorageUsed = FormatBytes(info.TotalBytes);
        LogFileCount = info.FileCount == 1 ? "1 arquivo" : $"{info.FileCount} arquivos";
        OldestLogDate = info.OldestEntryDate?.ToString("dd/MM/yyyy HH:mm") ?? "Sem registros";
        _lastStorageRefresh = DateTime.Now;
    }

    private static int NormalizeRetention(int days) =>
        new[] { 7, 14, 30, 60, 90 }.Contains(days) ? days : 14;

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.0} KB";
        return $"{bytes / (1024d * 1024d):0.0} MB";
    }
}

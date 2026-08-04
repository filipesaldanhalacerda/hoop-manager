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

    public ObservableCollection<LogEntry> Entries { get; } = new();
    public ICollectionView FilteredEntries { get; }
    public IReadOnlyList<string> Levels { get; } = ["Todos", "INFO", "WARN", "ERROR"];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedLevel = "Todos";
    [ObservableProperty] private string _summary = "Nenhum evento";
    [ObservableProperty] private bool _followLatest = true;

    public string LogsDirectory => _logger.LogsDirectory;

    public LogsViewModel(ILoggerService logger)
    {
        _logger = logger;
        foreach (var entry in logger.GetRecentEntries())
        {
            Entries.Add(entry);
        }

        FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = FilterEntry;
        _logger.LogWritten += OnLogWritten;
        UpdateSummary();
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();
    partial void OnSelectedLevelChanged(string value) => RefreshFilter();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void OpenLogsFolder()
    {
        Process.Start(new ProcessStartInfo { FileName = LogsDirectory, UseShellExecute = true });
        _logger.LogInformation("Pasta de logs aberta pelo usuário.");
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
            UpdateSummary();
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
}

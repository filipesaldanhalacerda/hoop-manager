using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.ViewModels;

public sealed partial class SessionHistoryViewModel : ObservableObject
{
    private readonly ISessionHistoryService _historyService;
    private readonly DispatcherTimer _durationTimer;

    public ObservableCollection<SessionHistoryEntry> Entries { get; } = new();
    public ICollectionView FilteredEntries { get; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _summary = "Nenhuma sessão registrada";
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public int ActiveCount => Entries.Count(entry => entry.IsActive);
    public int CompletedCount => Entries.Count - ActiveCount;

    public SessionHistoryViewModel(ISessionHistoryService historyService)
    {
        _historyService = historyService;
        FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = FilterEntry;
        RefreshEntries();
        historyService.HistoryChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(RefreshEntries);

        _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _durationTimer.Tick += (_, _) => RefreshEntries();
        _durationTimer.Start();
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        FilteredEntries.Refresh();
        UpdateSummary();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    private void RefreshEntries()
    {
        Entries.Clear();
        foreach (var entry in _historyService.GetEntries()) Entries.Add(entry);
        FilteredEntries.Refresh();
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(CompletedCount));
        UpdateSummary();
    }

    private bool FilterEntry(object item)
    {
        if (item is not SessionHistoryEntry entry) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var search = SearchText.Trim();
        return entry.ConnectionName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.EndReason.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.PortLabel.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSummary()
    {
        var visible = FilteredEntries.Cast<object>().Count();
        Summary = visible == 1 ? "1 sessão exibida" : $"{visible} sessões exibidas";
    }
}

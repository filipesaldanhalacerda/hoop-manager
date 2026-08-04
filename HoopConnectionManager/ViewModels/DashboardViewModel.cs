using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.ViewModels;

/// <summary>
/// ViewModel do dashboard principal.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IHoopService _hoopService;
    private readonly IConnectionService _connectionService;
    private readonly IDBeaverService _dbeaverService;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly ILoggerService _logger;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _userStatusMessage = "Buscando...";

    [ObservableProperty]
    private string? _userEmail;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();
    public ObservableCollection<ConnectionViewModel> FavoriteConnections { get; } = new();
    public ObservableCollection<ConnectionViewModel> RecentConnections { get; } = new();

    public ICollectionView FilteredConnections { get; }

    public DashboardViewModel(
        IHoopService hoopService,
        IConnectionService connectionService,
        IDBeaverService dbeaverService,
        ISettingsService settingsService,
        INotificationService notificationService,
        ILoggerService logger)
    {
        _hoopService = hoopService;
        _connectionService = connectionService;
        _dbeaverService = dbeaverService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _logger = logger;

        _connectionService.ActiveTunnelsChanged += (_, e) =>
        {
            UpdateConnectionStatus(e.ConnectionName, e.IsConnected);
        };

        FilteredConnections = CollectionViewSource.GetDefaultView(Connections);
        FilteredConnections.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConnectionViewModel.EnvironmentGroup)));
        FilteredConnections.SortDescriptions.Add(new SortDescription(nameof(ConnectionViewModel.EnvironmentGroup), ListSortDirection.Ascending));
        FilteredConnections.SortDescriptions.Add(new SortDescription(nameof(ConnectionViewModel.DisplayName), ListSortDirection.Ascending));

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchText))
            {
                FilteredConnections.Refresh();
            }
        };

        FilteredConnections.Filter = FilterConnection;
    }

    [RelayCommand]
    private async Task LoadConnectionsAsync()
    {
        IsBusy = true;
        UserStatusMessage = "Buscando...";

        try
        {
            var session = await _hoopService.GetSessionAsync();
            UserEmail = session.Email;
            UserStatusMessage = session.IsAuthenticated ? "Logado" : "Deslogado";

            var connections = await _hoopService.GetConnectionsAsync();
            Connections.Clear();

            var settings = _settingsService.Load();
            foreach (var connection in connections)
            {
                connection.IsFavorite = settings.FavoriteConnectionIds.Contains(connection.Id);
                Connections.Add(new ConnectionViewModel(connection));
            }

            RefreshFavoritesAndRecents();
            FilteredConnections.Refresh();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao carregar conexões.");
            UserStatusMessage = "Deslogado";
            _notificationService.Show($"Erro ao carregar conexões: {ex.Message}", NotificationLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync(ConnectionViewModel? connection)
    {
        if (connection is null)
        {
            return;
        }

        IsBusy = true;
        connection.Status = ConnectionStatus.Connecting;

        try
        {
            var tunnel = await _connectionService.ConnectAsync(connection.Name);
            connection.Status = tunnel.Status;
            connection.LastUsedAt = DateTime.Now;

            await PersistRecentConnectionAsync(connection);
            RefreshFavoritesAndRecents();

            if (tunnel.Credentials is not null && _settingsService.Load().OpenDBeaverAutomatically)
            {
                await _dbeaverService.OpenConnectionAsync(new DBeaverConnectionInfo
                {
                    ConnectionId = connection.Id,
                    ConnectionName = connection.Name,
                    Host = tunnel.Credentials.Host,
                    Port = tunnel.Credentials.Port,
                    Username = tunnel.Credentials.Username,
                    Password = tunnel.Credentials.Password
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Falha ao conectar a '{connection.Name}'.");
            connection.Status = ConnectionStatus.Error;
            _notificationService.Show($"Erro ao conectar: {ex.Message}", NotificationLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync(ConnectionViewModel? connection)
    {
        if (connection is null)
        {
            return;
        }

        await _connectionService.DisconnectAsync(connection.Name);
        connection.Status = ConnectionStatus.Disconnected;
        _notificationService.Show($"Desconectado de '{connection.Name}'.");
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(ConnectionViewModel? connection)
    {
        if (connection is null)
        {
            return;
        }

        connection.IsFavorite = !connection.IsFavorite;

        var settings = _settingsService.Load();
        if (connection.IsFavorite)
        {
            settings.FavoriteConnectionIds.Add(connection.Id);
        }
        else
        {
            settings.FavoriteConnectionIds.Remove(connection.Id);
        }

        await _settingsService.SaveAsync(settings);
        RefreshFavoritesAndRecents();
    }

    [RelayCommand]
    private void CopyName(ConnectionViewModel? connection)
    {
        if (connection is null)
        {
            return;
        }

        System.Windows.Clipboard.SetText(connection.Name);
        _notificationService.Show($"Nome '{connection.Name}' copiado.");
    }

    [RelayCommand]
    private async Task RefreshUserStatusAsync()
    {
        var session = await _hoopService.GetSessionAsync();
        UserEmail = session.Email;
        UserStatusMessage = session.IsAuthenticated ? "Logado" : "Deslogado";
    }

    private bool FilterConnection(object? obj)
    {
        if (obj is not ConnectionViewModel connection)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var query = SearchText.Trim();
        return connection.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || connection.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || connection.EnvironmentGroup.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private async Task PersistRecentConnectionAsync(ConnectionViewModel connection)
    {
        var settings = _settingsService.Load();
        settings.RecentConnectionIds.Remove(connection.Id);
        settings.RecentConnectionIds.Insert(0, connection.Id);

        if (settings.RecentConnectionIds.Count > 10)
        {
            settings.RecentConnectionIds = settings.RecentConnectionIds.Take(10).ToList();
        }

        await _settingsService.SaveAsync(settings);
    }

    private void UpdateConnectionStatus(string connectionName, bool isConnected)
    {
        var connection = Connections.FirstOrDefault(c => c.Name == connectionName);
        if (connection is not null)
        {
            connection.Status = isConnected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
        }
    }

    private void RefreshFavoritesAndRecents()
    {
        FavoriteConnections.Clear();
        foreach (var favorite in Connections.Where(c => c.IsFavorite).OrderBy(c => c.DisplayName))
        {
            FavoriteConnections.Add(favorite);
        }

        var settings = _settingsService.Load();
        RecentConnections.Clear();
        foreach (var recentId in settings.RecentConnectionIds)
        {
            var recent = Connections.FirstOrDefault(c => c.Id == recentId);
            if (recent is not null)
            {
                RecentConnections.Add(recent);
            }
        }
    }
}

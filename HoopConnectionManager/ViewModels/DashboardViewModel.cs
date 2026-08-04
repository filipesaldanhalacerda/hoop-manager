using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _statusRefreshTimer;
    private readonly DispatcherTimer _connectionsRefreshTimer;
    private readonly Dictionary<string, (ConnectionStatus Status, string? Detail)> _connectionStates =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _statusRefreshInProgress;
    private bool _connectionsRefreshInProgress;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _searchResultSummary = "Carregando catálogo...";

    [ObservableProperty]
    private string _catalogSyncStatus = "Preparando sincronização...";

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    [ObservableProperty]
    private string _userStatusMessage = "Buscando...";

    [ObservableProperty]
    private string? _userEmail;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _connectedCount;

    [ObservableProperty]
    private int _totalConnections;

    [ObservableProperty]
    private string _operationalStatus = "Nenhum túnel ativo";

    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();
    public ObservableCollection<ConnectionViewModel> ActiveConnections { get; } = new();
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
        _settingsService.SettingsSaved += (_, settings) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                ConfigureConnectionsRefresh(settings.RefreshConnectionsOnStartup));

        _connectionService.ActiveTunnelsChanged += (_, e) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateConnectionStatus(e.ConnectionName, e.Status, e.Detail);
                SynchronizeActiveConnections();
            });
        };

        FilteredConnections = CollectionViewSource.GetDefaultView(Connections);
        FilteredConnections.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConnectionViewModel.EnvironmentGroup)));
        FilteredConnections.SortDescriptions.Add(new SortDescription(nameof(ConnectionViewModel.EnvironmentGroup), ListSortDirection.Ascending));
        FilteredConnections.SortDescriptions.Add(new SortDescription(nameof(ConnectionViewModel.DisplayName), ListSortDirection.Ascending));

        FilteredConnections.Filter = FilterConnection;

        _statusRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(2)
        };
        _statusRefreshTimer.Tick += async (_, _) => await RefreshStatusAutomaticallyAsync();
        _statusRefreshTimer.Start();

        _connectionsRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _connectionsRefreshTimer.Tick += async (_, _) => await LoadConnectionsAsync(showProgress: false);
        ConfigureConnectionsRefresh(_settingsService.Load().RefreshConnectionsOnStartup);

        _ = LoadConnectionsAsync(showProgress: true);
    }

    private void ConfigureConnectionsRefresh(bool enabled)
    {
        if (enabled)
        {
            _connectionsRefreshTimer.Start();
            CatalogSyncStatus = "Sincronização automática ativa • a cada 5 min";
        }
        else
        {
            _connectionsRefreshTimer.Stop();
            CatalogSyncStatus = "Sincronização automática desativada";
        }
    }

    private async Task LoadConnectionsAsync(bool showProgress)
    {
        if (_connectionsRefreshInProgress || (!showProgress && IsBusy))
        {
            return;
        }

        _connectionsRefreshInProgress = true;
        if (showProgress)
        {
            IsBusy = true;
            UserStatusMessage = "Buscando...";
        }

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
                var viewModel = new ConnectionViewModel(connection);
                if (_connectionService.ActiveTunnels.TryGetValue(connection.Name, out var tunnel)
                    && tunnel.Credentials is not null)
                {
                    viewModel.SetCredentials(tunnel.Credentials);
                    viewModel.ConnectedAt = tunnel.StartedAt;
                    viewModel.Status = ConnectionStatus.Connected;
                }
                else if (_connectionStates.TryGetValue(connection.Name, out var state))
                {
                    viewModel.Status = state.Status;
                    viewModel.StatusDetail = state.Detail;
                }
                Connections.Add(viewModel);
            }

            RefreshFavoritesAndRecents();
            SynchronizeActiveConnections();
            FilteredConnections.Refresh();
            UpdateSearchResultSummary();
            CatalogSyncStatus = _connectionsRefreshTimer.IsEnabled
                ? $"Atualizado às {DateTime.Now:HH:mm} • automático a cada 5 min"
                : $"Atualizado às {DateTime.Now:HH:mm} • sincronização automática desativada";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao carregar conexões.");
            CatalogSyncStatus = "Falha na última sincronização • nova tentativa automática";
            if (showProgress)
            {
                UserStatusMessage = "Deslogado";
                _notificationService.Show($"Erro ao carregar conexões: {ex.Message}", NotificationLevel.Error);
            }
        }
        finally
        {
            _connectionsRefreshInProgress = false;
            if (showProgress)
            {
                IsBusy = false;
            }
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchText));
        FilteredConnections.Refresh();
        UpdateSearchResultSummary();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

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
            if (tunnel.Credentials is not null) connection.SetCredentials(tunnel.Credentials);
            connection.ConnectedAt = tunnel.StartedAt;
            connection.LastUsedAt = DateTime.Now;
            SynchronizeActiveConnections();

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

        IsBusy = true;
        try
        {
            await _connectionService.DisconnectAsync(connection.Name);
            connection.ClearCredentials();
            connection.ConnectedAt = null;
            connection.Status = ConnectionStatus.Disconnected;
            SynchronizeActiveConnections();
            _notificationService.Show($"Túnel de '{connection.DisplayName}' desconectado.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Falha ao desconectar '{connection.Name}'.");
            _notificationService.Show($"Erro ao desconectar: {ex.Message}", NotificationLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
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
    private void CopyHost(ConnectionViewModel? connection) => CopyTemporary(connection?.Host, "Host");
    [RelayCommand]
    private void CopyPort(ConnectionViewModel? connection) => CopyTemporary(connection?.Port?.ToString(), "Porta");
    [RelayCommand]
    private void CopyUsername(ConnectionViewModel? connection) => CopyTemporary(connection?.Username, "Usuário");
    [RelayCommand]
    private void CopyPassword(ConnectionViewModel? connection) => CopyTemporary(connection?.Password, "Senha");

    private void CopyTemporary(string? value, string label)
    {
        if (string.IsNullOrEmpty(value)) { _notificationService.Show("Conecte primeiro para obter os dados temporários.", NotificationLevel.Warning); return; }
        System.Windows.Clipboard.SetText(value);
        _notificationService.Show($"{label} copiado para a área de transferência.");
    }

    private async Task RefreshStatusAutomaticallyAsync()
    {
        if (_statusRefreshInProgress)
        {
            return;
        }

        _statusRefreshInProgress = true;
        try
        {
            // A sincronização dos processos é local e praticamente gratuita.
            SynchronizeActiveConnections();

            // A consulta remota ocorre somente a cada dois minutos.
            var session = await _hoopService.GetSessionAsync();
            UserEmail = session.Email;
            UserStatusMessage = session.IsAuthenticated ? "Logado" : "Deslogado";
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Não foi possível atualizar o status automaticamente: {ex.Message}");
        }
        finally
        {
            _statusRefreshInProgress = false;
        }
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
            || connection.EnvironmentGroup.Contains(query, StringComparison.OrdinalIgnoreCase)
            || connection.Type.Contains(query, StringComparison.OrdinalIgnoreCase)
            || connection.ConnectionStateLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
            || connection.LocalEndpoint.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSearchResultSummary()
    {
        var visibleCount = FilteredConnections.Cast<object>().Count();
        SearchResultSummary = HasSearchText
            ? $"{visibleCount} de {Connections.Count} conexões encontradas"
            : $"{Connections.Count} conexões disponíveis";
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

    private void UpdateConnectionStatus(string connectionName, ConnectionStatus status, string? detail)
    {
        _connectionStates[connectionName] = (status, detail);
        var connection = Connections.FirstOrDefault(c => c.Name == connectionName);
        if (connection is not null)
        {
            connection.Status = status;
            connection.StatusDetail = detail;
            if (status != ConnectionStatus.Connected)
            {
                connection.ClearCredentials();
                connection.ConnectedAt = null;
            }
        }
    }

    private void SynchronizeActiveConnections()
    {
        ActiveConnections.Clear();
        foreach (var connection in Connections.Where(c => _connectionService.IsConnected(c.Name)).OrderBy(c => c.DisplayName))
        {
            if (_connectionService.ActiveTunnels.TryGetValue(connection.Name, out var tunnel)
                && tunnel.Credentials is not null)
            {
                connection.SetCredentials(tunnel.Credentials);
                connection.ConnectedAt = tunnel.StartedAt;
                connection.Status = ConnectionStatus.Connected;
            }
            ActiveConnections.Add(connection);
        }

        ConnectedCount = ActiveConnections.Count;
        TotalConnections = Connections.Count;
        OperationalStatus = ConnectedCount == 0
            ? "Nenhum túnel ativo"
            : ConnectedCount == 1 ? "1 túnel ativo" : $"{ConnectedCount} túneis ativos";
        FilteredConnections.Refresh();
        UpdateSearchResultSummary();
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

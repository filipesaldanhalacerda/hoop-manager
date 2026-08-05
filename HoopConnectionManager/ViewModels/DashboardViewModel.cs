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
    private readonly INavigationService _navigationService;
    private readonly IFirstRunService _firstRunService;
    private readonly DispatcherTimer _statusRefreshTimer;
    private readonly DispatcherTimer _connectionsRefreshTimer;
    private readonly Dictionary<string, (ConnectionStatus Status, string? Detail)> _connectionStates =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _statusRefreshInProgress;
    private bool _connectionsRefreshInProgress;
    private bool _automaticCatalogRefreshEnabled;
    private bool? _lastAuthenticationState;
    private CancellationTokenSource? _clipboardClearCancellation;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _searchResultSummary = "Carregando catálogo...";

    [ObservableProperty]
    private string _catalogSyncStatus = "Preparando sincronização...";

    /// <summary>
    /// Estado do último ciclo de sincronização: <c>Ok</c>, <c>Error</c> ou <c>Neutral</c>.
    /// A tela pintava o distintivo sempre de verde, inclusive ao anunciar falha.
    /// </summary>
    [ObservableProperty]
    private string _catalogSyncState = "Neutral";

    [ObservableProperty]
    private string? _catalogMessage;

    public bool HasCatalogMessage => !string.IsNullOrWhiteSpace(CatalogMessage);

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

    /// <summary>
    /// Começa como pronto para a faixa não piscar antes da primeira verificação.
    /// </summary>
    [ObservableProperty]
    private bool _isEnvironmentReady = true;

    [ObservableProperty]
    private string _environmentPendingSummary = string.Empty;

    public bool HasPendingSetup => !IsEnvironmentReady;

    partial void OnIsEnvironmentReadyChanged(bool value) => OnPropertyChanged(nameof(HasPendingSetup));

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
        ILoggerService logger,
        ILoginService loginService,
        INavigationService navigationService,
        IFirstRunService firstRunService)
    {
        _hoopService = hoopService;
        _connectionService = connectionService;
        _dbeaverService = dbeaverService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _logger = logger;
        _navigationService = navigationService;
        _firstRunService = firstRunService;

        // Reavalia sempre que esta tela volta a ser exibida: é o retorno do assistente
        // que precisa fazer a faixa de pendência desaparecer imediatamente.
        _navigationService.Navigated += (_, e) =>
        {
            if (ReferenceEquals(e.ViewModel, this))
            {
                _ = RefreshEnvironmentReadinessAsync();
            }
        };

        loginService.AuthenticationSucceeded += (_, _) =>
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                CatalogSyncStatus = "Autenticação renovada • atualizando conexões...";
                CatalogSyncState = "Neutral";
                _ = LoadConnectionsAsync(showProgress: false);
                _ = RefreshEnvironmentReadinessAsync();
            }));
        };
        _settingsService.SettingsSaved += (_, settings) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                ConfigureConnectionsRefresh(settings.RefreshConnectionsOnStartup));

        _connectionService.ActiveTunnelsChanged += (_, e) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateConnectionStatus(e.ConnectionName, e.Status, e.Detail);
                SynchronizeActiveConnections();
                if (e.TunnelRecreated)
                {
                    _ = UpdateDBeaverAfterReconnectionAsync(e.ConnectionName);
                }
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
        _ = RefreshEnvironmentReadinessAsync();
    }

    /// <summary>
    /// Mantém a faixa de configuração pendente em dia. Enquanto o Hoop, a sessão ou o
    /// DBeaver estiverem faltando, o dev continua tendo um caminho de volta ao assistente.
    /// </summary>
    private async Task RefreshEnvironmentReadinessAsync()
    {
        try
        {
            var readiness = await _firstRunService.EvaluateReadinessAsync();
            IsEnvironmentReady = readiness.IsReady;
            EnvironmentPendingSummary = readiness.Summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Não foi possível avaliar o estado do ambiente: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenGuidedSetup()
    {
        _navigationService.NavigateTo<WizardViewModel>();
        if (_navigationService.CurrentViewModel is WizardViewModel wizard)
        {
            _ = wizard.PrepareAsync();
        }
    }

    private async Task UpdateDBeaverAfterReconnectionAsync(string connectionName)
    {
        if (!_settingsService.Load().OpenDBeaverAutomatically
            || !_connectionService.ActiveTunnels.TryGetValue(connectionName, out var tunnel)
            || tunnel.Credentials is null)
        {
            return;
        }

        var connection = Connections.FirstOrDefault(item =>
            string.Equals(item.Name, connectionName, StringComparison.OrdinalIgnoreCase));
        try
        {
            var synchronized = await _dbeaverService.UpdateConnectionConfigurationAsync(new DBeaverConnectionInfo
            {
                ConnectionId = connection?.Id ?? connectionName,
                ConnectionName = connectionName,
                Host = tunnel.Credentials.Host,
                Port = tunnel.Credentials.Port,
                Username = tunnel.Credentials.Username,
                Password = tunnel.Credentials.Password
            });

            if (synchronized)
            {
                _logger.LogInformation($"DBeaver sincronizado após a recriação do túnel '{connectionName}'.");
                return;
            }

            // A porta e a senha mudam a cada túnel: sem confirmação, o DBeaver ficou
            // com dados que não conectam mais e o usuário precisa saber disso.
            _logger.LogWarning($"O túnel '{connectionName}' foi recuperado, mas o DBeaver não confirmou a nova porta/senha.");
            _notificationService.Show(
                "Túnel recuperado, mas o DBeaver não confirmou a nova porta/senha. Abra a conexão novamente.",
                NotificationLevel.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"O túnel '{connectionName}' foi recuperado, mas o DBeaver não pôde ser atualizado: {ex.Message}");
            _notificationService.Show(
                "Túnel recuperado, mas o DBeaver não recebeu a nova porta/senha. Abra a conexão novamente.",
                NotificationLevel.Warning);
        }
    }

    private void ConfigureConnectionsRefresh(bool enabled)
    {
        _automaticCatalogRefreshEnabled = enabled;
        if (enabled)
        {
            _connectionsRefreshTimer.Interval = TimeSpan.FromMinutes(5);
            _connectionsRefreshTimer.Start();
            CatalogSyncStatus = "Sincronização automática ativa • a cada 5 min";
            CatalogSyncState = "Ok";
        }
        else
        {
            _connectionsRefreshTimer.Stop();
            CatalogSyncStatus = "Sincronização automática desativada";
            CatalogSyncState = "Neutral";
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
            CatalogMessage = null;
            var session = await _hoopService.GetSessionAsync();
            UserEmail = session.Email;
            UserStatusMessage = session.IsAuthenticated ? "Logado" : "Deslogado";
            _lastAuthenticationState = session.IsAuthenticated;

            if (!session.IsAuthenticated)
            {
                throw new InvalidOperationException("A sessão do Hoop não está autenticada. O sistema continuará tentando e atualizará a tela quando o acesso voltar.");
            }

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

            if (connections.Count == 0)
            {
                CatalogMessage = "O Hoop respondeu, mas não retornou conexões autorizadas. Confirme a autenticação e consulte os registros para ver a resposta do CLI.";
            }

            RefreshFavoritesAndRecents();
            SynchronizeActiveConnections();
            FilteredConnections.Refresh();
            UpdateSearchResultSummary();
            CatalogSyncStatus = _connectionsRefreshTimer.IsEnabled
                ? $"Atualizado às {DateTime.Now:HH:mm} • automático a cada 5 min"
                : $"Atualizado às {DateTime.Now:HH:mm} • sincronização automática desativada";
            CatalogSyncState = "Ok";
            RestoreNormalCatalogRefresh();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao carregar conexões.");
            CatalogSyncStatus = "Falha na última sincronização • nova tentativa automática";
            CatalogSyncState = "Error";
            CatalogMessage = $"Não foi possível carregar o catálogo. {ex.Message}";
            EnableCatalogRecoveryRefresh();
            if (showProgress)
            {
                UserStatusMessage = "Deslogado";
                await ShowGuidedFailureAsync("Falha ao carregar conexões", ex);
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

    private void EnableCatalogRecoveryRefresh()
    {
        CatalogSyncState = "Error";
        if (!_automaticCatalogRefreshEnabled)
        {
            CatalogSyncStatus = "Falha na última sincronização • atualização automática desativada";
            return;
        }

        _connectionsRefreshTimer.Stop();
        _connectionsRefreshTimer.Interval = TimeSpan.FromSeconds(20);
        _connectionsRefreshTimer.Start();
        CatalogSyncStatus = "Hoop indisponível • tentando novamente a cada 20 s";
    }

    private void RestoreNormalCatalogRefresh()
    {
        if (!_automaticCatalogRefreshEnabled)
        {
            return;
        }

        _connectionsRefreshTimer.Stop();
        _connectionsRefreshTimer.Interval = TimeSpan.FromMinutes(5);
        _connectionsRefreshTimer.Start();
    }

    partial void OnCatalogMessageChanged(string? value) => OnPropertyChanged(nameof(HasCatalogMessage));

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

            if (tunnel.UsedAlternativePort && tunnel.Credentials is not null)
            {
                _notificationService.Show(
                    $"Porta local 5433 ocupada — escolhendo automaticamente a porta {tunnel.Credentials.Port}.",
                    NotificationLevel.Information);
            }

            if (tunnel.Credentials is not null && _settingsService.Load().OpenDBeaverAutomatically)
            {
                try
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
                catch (Exception ex)
                {
                    _logger.LogWarning($"Túnel conectado, mas o DBeaver não pôde ser aberto: {ex.Message}");
                    _notificationService.Show(
                        "DBeaver não encontrado — selecione o executável nas Configurações. O túnel continua conectado.",
                        NotificationLevel.Warning,
                        NotificationAction.SelectDBeaver);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Falha ao conectar a '{connection.Name}'.");
            connection.Status = ConnectionStatus.Error;
            await ShowGuidedFailureAsync("Falha ao conectar", ex);
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
            _notificationService.Show($"Túnel de '{connection.DisplayName}' encerrado e porta local fechada. O DBeaver atualizará o estado na próxima operação.");
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

    private async Task ShowGuidedFailureAsync(string context, Exception exception)
    {
        // Antes de falar em sessão expirada, confirmar que houve sessão alguma vez.
        // Sem o Hoop instalado, GetSessionAsync devolve "não autenticado" e a mensagem
        // caía no ramo de expiração, oferecendo um "Autenticar novamente" que só falha.
        await RefreshEnvironmentReadinessAsync();
        if (HasPendingSetup)
        {
            _notificationService.Show(
                $"Configuração incompleta. {EnvironmentPendingSummary}",
                NotificationLevel.Warning,
                NotificationAction.OpenGuidedSetup);
            return;
        }

        var message = exception.Message;
        if (ContainsAny(message, "unauthorized", "unauthenticated", "token", "login", "sessão", "autentica"))
        {
            _notificationService.Show(
                "Sessão expirada — autentique novamente para continuar.",
                NotificationLevel.Warning,
                NotificationAction.Reauthenticate);
            return;
        }

        if (ContainsAny(message, "gateway", "vpn", "network", "rede", "timeout", "timed out", "unavailable", "connection refused"))
        {
            _notificationService.Show(
                "Gateway inacessível — confira a VPN e a rede corporativa. O sistema tentará novamente automaticamente.",
                NotificationLevel.Error,
                NotificationAction.OpenSettings);
            return;
        }

        _notificationService.Show($"{context}: {message}", NotificationLevel.Error);
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private async Task ToggleFavoriteAsync(ConnectionViewModel? connection)
    {
        if (connection is null)
        {
            return;
        }

        connection.IsFavorite = !connection.IsFavorite;

        await _settingsService.UpdateAsync(settings =>
        {
            settings.FavoriteConnectionIds.Remove(connection.Id);
            if (connection.IsFavorite)
            {
                settings.FavoriteConnectionIds.Add(connection.Id);
            }
        });
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
        try
        {
            System.Windows.Clipboard.SetText(value);
            if (label == "Senha")
            {
                var cancellation = new CancellationTokenSource();
                var previous = Interlocked.Exchange(ref _clipboardClearCancellation, cancellation);
                previous?.Cancel();
                previous?.Dispose();
                _ = ClearSensitiveClipboardAsync(value, cancellation.Token);
                _notificationService.Show("Senha copiada. Ela será removida da área de transferência em 30 segundos.");
            }
            else
            {
                _notificationService.Show($"{label} copiado para a área de transferência.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Não foi possível copiar {label.ToLowerInvariant()}: {ex.Message}");
            _notificationService.Show("O Windows não permitiu acessar a área de transferência.", NotificationLevel.Warning);
        }
    }

    private static async Task ClearSensitiveClipboardAsync(string copiedValue, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            if (System.Windows.Clipboard.ContainsText()
                && string.Equals(System.Windows.Clipboard.GetText(), copiedValue, StringComparison.Ordinal))
            {
                System.Windows.Clipboard.Clear();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (System.Runtime.InteropServices.COMException) { }
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
            var authenticationRestored = session.IsAuthenticated && _lastAuthenticationState == false;
            _lastAuthenticationState = session.IsAuthenticated;
            if (authenticationRestored)
            {
                await LoadConnectionsAsync(showProgress: false);
            }

            await RefreshEnvironmentReadinessAsync();
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
        await _settingsService.UpdateAsync(settings =>
        {
            settings.RecentConnectionIds.Remove(connection.Id);
            settings.RecentConnectionIds.Insert(0, connection.Id);
            if (settings.RecentConnectionIds.Count > 10)
            {
                settings.RecentConnectionIds = settings.RecentConnectionIds.Take(10).ToList();
            }
        });
    }

    private void UpdateConnectionStatus(string connectionName, ConnectionStatus status, string? detail)
    {
        _connectionStates[connectionName] = (status, detail);
        var connection = Connections.FirstOrDefault(c => c.Name == connectionName);
        if (connection is not null)
        {
            connection.Status = status;
            connection.StatusDetail = detail;
            if (status is not (ConnectionStatus.Connected or ConnectionStatus.Degraded))
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
                connection.Status = tunnel.Status;
            }
            ActiveConnections.Add(connection);
        }

        ConnectedCount = ActiveConnections.Count;
        TotalConnections = Connections.Count;
        var degradedCount = ActiveConnections.Count(connection => connection.Status == ConnectionStatus.Degraded);
        OperationalStatus = degradedCount > 0
            ? $"{degradedCount} conexão(ões) instável(is)"
            : ConnectedCount == 0
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

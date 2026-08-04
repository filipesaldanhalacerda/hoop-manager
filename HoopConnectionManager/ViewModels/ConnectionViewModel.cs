using CommunityToolkit.Mvvm.ComponentModel;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.ViewModels;

/// <summary>
/// ViewModel wrapper de uma conexão para a UI.
/// </summary>
public sealed partial class ConnectionViewModel : ObservableObject
{
    private readonly Connection _connection;

    [ObservableProperty]
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    [ObservableProperty] private string? _host;
    [ObservableProperty] private int? _port;
    [ObservableProperty] private string? _username;
    [ObservableProperty] private string? _password;
    [ObservableProperty] private DateTime? _connectedAt;

    public bool CanConnect => Status is ConnectionStatus.Disconnected or ConnectionStatus.Error;
    public bool CanDisconnect => Status == ConnectionStatus.Connected;
    public string LocalEndpoint => Host is not null && Port is not null ? $"{Host}:{Port}" : "Aguardando túnel";
    public string ConnectionStateLabel => Status switch
    {
        ConnectionStatus.Connecting => "Conectando",
        ConnectionStatus.Connected => "Conectado",
        ConnectionStatus.Error => "Erro",
        _ => "Desconectado"
    };

    public void SetCredentials(ConnectionCredentials credentials)
    { Host = credentials.Host; Port = credentials.Port; Username = credentials.Username; Password = credentials.Password; }

    public void ClearCredentials() { Host = null; Port = null; Username = null; Password = null; }

    partial void OnStatusChanged(ConnectionStatus value)
    {
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(ConnectionStateLabel));
    }

    partial void OnHostChanged(string? value) => OnPropertyChanged(nameof(LocalEndpoint));
    partial void OnPortChanged(int? value) => OnPropertyChanged(nameof(LocalEndpoint));

    public ConnectionViewModel(Connection connection)
    {
        _connection = connection;
    }

    public string Id => _connection.Id;
    public string Name => _connection.Name;
    public string DisplayName => _connection.DisplayName;
    public EnvironmentType Environment => _connection.Environment;
    public string EnvironmentGroup => _connection.EnvironmentGroup;
    public string Type => _connection.Type;

    public bool IsFavorite
    {
        get => _connection.IsFavorite;
        set => SetProperty(_connection.IsFavorite, value, _connection, (c, v) => c.IsFavorite = v);
    }

    public DateTime? LastUsedAt
    {
        get => _connection.LastUsedAt;
        set => SetProperty(_connection.LastUsedAt, value, _connection, (c, v) => c.LastUsedAt = v);
    }
}

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

    public void SetCredentials(ConnectionCredentials credentials)
    { Host = credentials.Host; Port = credentials.Port; Username = credentials.Username; Password = credentials.Password; }

    public void ClearCredentials() { Host = null; Port = null; Username = null; Password = null; }

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

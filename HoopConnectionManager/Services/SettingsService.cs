using System.IO;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Services.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do serviço de configurações locais.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsDirectory;
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly object _cacheLock = new();
    private ApplicationSettings? _cache;
    private (long Ticks, long Length) _cacheStamp;

    public event EventHandler<ApplicationSettings>? SettingsSaved;

    public SettingsService(string? baseDirectory = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsDirectory = baseDirectory ?? Path.Combine(localAppData, ApplicationConstants.StorageRootName, ApplicationConstants.DataDirectoryName);
        _settingsPath = Path.Combine(_settingsDirectory, ApplicationConstants.SettingsFileName);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string GetSettingsPath() => _settingsPath;

    /// <summary>
    /// Devolve as configurações atuais. O conteúdo fica em memória e só é relido
    /// quando o arquivo muda, porque este método é chamado nos caminhos quentes
    /// (verificação de instalação, monitor de saúde e atualização de catálogo).
    /// </summary>
    public ApplicationSettings Load()
    {
        lock (_cacheLock)
        {
            var stamp = ReadStamp();
            if (_cache is not null && stamp == _cacheStamp)
            {
                return Clone(_cache);
            }

            var settings = ReadFromDisk();
            _cache = settings;
            _cacheStamp = stamp;
            return Clone(settings);
        }
    }

    private ApplicationSettings ReadFromDisk()
    {
        if (!File.Exists(_settingsPath))
        {
            return new ApplicationSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, _jsonOptions) ?? new ApplicationSettings();
            settings.FavoriteConnectionIds ??= [];
            settings.RecentConnectionIds ??= [];
            return settings;
        }
        catch (JsonException) { return new ApplicationSettings(); }
        catch (IOException) { return new ApplicationSettings(); }
        catch (UnauthorizedAccessException) { return new ApplicationSettings(); }
    }

    /// <summary>
    /// Identidade barata do arquivo: detecta gravações feitas por outra instância
    /// sem pagar leitura e desserialização a cada consulta.
    /// </summary>
    private (long Ticks, long Length) ReadStamp()
    {
        try
        {
            var info = new FileInfo(_settingsPath);
            return info.Exists ? (info.LastWriteTimeUtc.Ticks, info.Length) : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    /// <summary>
    /// O cache nunca é entregue por referência: quem chama pode alterar o objeto.
    /// </summary>
    private static ApplicationSettings Clone(ApplicationSettings settings) => new()
    {
        HoopExecutablePath = settings.HoopExecutablePath,
        DBeaverExecutablePath = settings.DBeaverExecutablePath,
        StartWithWindows = settings.StartWithWindows,
        MinimizeToTray = settings.MinimizeToTray,
        DisconnectTunnelsOnExit = settings.DisconnectTunnelsOnExit,
        OpenDBeaverAutomatically = settings.OpenDBeaverAutomatically,
        RefreshConnectionsOnStartup = settings.RefreshConnectionsOnStartup,
        LogRetentionDays = settings.LogRetentionDays,
        Theme = settings.Theme,
        IsFirstRunCompleted = settings.IsFirstRunCompleted,
        FavoriteConnectionIds = [.. settings.FavoriteConnectionIds],
        RecentConnectionIds = [.. settings.RecentConnectionIds]
    };

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            await WriteSettingsCoreAsync(settings, cancellationToken);
        }
        finally
        {
            _saveLock.Release();
        }

        SettingsSaved?.Invoke(this, settings);
    }

    public async Task UpdateAsync(Action<ApplicationSettings> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ApplicationSettings settings;
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            settings = Load();
            update(settings);
            await WriteSettingsCoreAsync(settings, cancellationToken);
        }
        finally
        {
            _saveLock.Release();
        }

        SettingsSaved?.Invoke(this, settings);
    }

    private async Task WriteSettingsCoreAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settingsDirectory);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        var temporaryPath = _settingsPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _settingsPath, true);

        lock (_cacheLock)
        {
            _cache = Clone(settings);
            _cacheStamp = ReadStamp();
        }
    }
}

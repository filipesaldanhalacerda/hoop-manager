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

    public SettingsService(string? baseDirectory = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsDirectory = baseDirectory ?? Path.Combine(localAppData, ApplicationConstants.ApplicationName, ApplicationConstants.DataDirectoryName);
        _settingsPath = Path.Combine(_settingsDirectory, ApplicationConstants.SettingsFileName);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string GetSettingsPath() => _settingsPath;

    public ApplicationSettings Load()
    {
        Directory.CreateDirectory(_settingsDirectory);

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
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settingsDirectory);

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        var temporaryPath = _settingsPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _settingsPath, true);
    }
}

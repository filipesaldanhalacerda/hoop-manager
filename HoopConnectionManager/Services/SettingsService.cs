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

    public SettingsService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsDirectory = Path.Combine(localAppData, ApplicationConstants.ApplicationName, ApplicationConstants.DataDirectoryName);
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

        var json = File.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<ApplicationSettings>(json, _jsonOptions) ?? new ApplicationSettings();
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settingsDirectory);

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
    }
}

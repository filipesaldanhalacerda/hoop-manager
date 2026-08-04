using HoopConnectionManager.Configuration;

namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Responsável por salvar e carregar configurações locais em JSON.
/// Nunca armazena credenciais.
/// </summary>
public interface ISettingsService
{
    ApplicationSettings Load();
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
    string GetSettingsPath();
}

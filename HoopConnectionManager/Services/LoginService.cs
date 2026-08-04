using System.IO;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do serviço de login no Hoop.
/// </summary>
public sealed class LoginService : ILoginService
{
    private readonly IHoopService _hoopService;
    private readonly ICommandRunner _commandRunner;
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;

    public event EventHandler? AuthenticationSucceeded;

    public LoginService(
        IHoopService hoopService,
        ICommandRunner commandRunner,
        ISettingsService settingsService,
        ILoggerService logger)
    {
        _hoopService = hoopService;
        _commandRunner = commandRunner;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<bool> LoginAsync(CancellationToken cancellationToken = default)
    {
        if (!await _hoopService.IsInstalledAsync(cancellationToken))
        {
            throw new InvalidOperationException("Hoop não está instalado.");
        }

        var hoopPath = _settingsService.Load().HoopExecutablePath;
        if (string.IsNullOrWhiteSpace(hoopPath) || !File.Exists(hoopPath))
        {
            throw new InvalidOperationException("Caminho do hoop.exe não encontrado.");
        }

        _logger.LogInformation("Iniciando login no Hoop.");

        var result = await _commandRunner.RunAsync(
            hoopPath,
            "login",
            cancellationToken: cancellationToken,
            timeout: TimeSpan.FromMinutes(5));

        if (!result.Success)
        {
            _logger.LogError($"Falha no login: {result.StandardError}");
            return false;
        }

        // O próprio comando aguarda a conclusão do fluxo no navegador e só retorna
        // sucesso depois de salvar a autenticação. Versões como a 1.51.2 não expõem
        // o comando `whoami`, portanto não devemos transformar um login concluído
        // em falso negativo tentando executá-lo em seguida.
        _logger.LogInformation("Login no Hoop concluído com sucesso.");
        AuthenticationSucceeded?.Invoke(this, EventArgs.Empty);
        return true;
    }
}

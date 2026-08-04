using System.Diagnostics;

namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Resultado da execução de um comando externo.
/// </summary>
public sealed class CommandResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public bool Success => ExitCode == 0;
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Responsável por executar comandos externos de forma assíncrona,
/// com suporte a timeout e cancelamento.
/// </summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        IProgress<string>? outputProgress = null);
}

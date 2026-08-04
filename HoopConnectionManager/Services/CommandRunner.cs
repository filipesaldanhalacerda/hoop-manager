using System.Diagnostics;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão do executor de comandos externos.
/// Captura stdout/stderr, respeita timeout e cancelamento.
/// </summary>
public sealed class CommandRunner : ICommandRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        IProgress<string>? outputProgress = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
            throw new InvalidOperationException($"Não foi possível iniciar '{fileName}'.");

        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); outputProgress?.Report(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { error.AppendLine(e.Data); outputProgress?.Report(e.Data); } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested)
                throw;

            throw new TimeoutException($"O comando '{fileName} {arguments}' excedeu o timeout de {effectiveTimeout.TotalSeconds}s.");
        }

        stopwatch.Stop();

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = output.ToString(),
            StandardError = error.ToString(),
            Duration = stopwatch.Elapsed
        };
    }
}

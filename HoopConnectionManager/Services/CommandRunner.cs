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
        TimeSpan? timeout = null)
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

        var tcs = new TaskCompletionSource<object?>();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcs.TrySetResult(null);

        process.Start();

        try
        {
            await using (cts.Token.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                tcs.TrySetCanceled(cts.Token);
            }))
            {
                await tcs.Task;
            }
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;

            throw new TimeoutException($"O comando '{fileName} {arguments}' excedeu o timeout de {effectiveTimeout.TotalSeconds}s.");
        }

        var output = await process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = await process.StandardError.ReadToEndAsync(CancellationToken.None);
        stopwatch.Stop();

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = output,
            StandardError = error,
            Duration = stopwatch.Elapsed
        };
    }
}

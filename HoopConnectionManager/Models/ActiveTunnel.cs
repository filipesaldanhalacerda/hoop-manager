using System.Diagnostics;

namespace HoopConnectionManager.Models;

/// <summary>Representa um túnel Hoop ativo mantido em memória.</summary>
public sealed class ActiveTunnel : IDisposable
{
    /// <summary>Limite para o encerramento síncrono feito na saída do aplicativo.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private Action? _releaseResources;
    private int _disposed;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ConnectionName { get; init; } = string.Empty;
    public Process? Process { get; init; }
    public ConnectionCredentials? Credentials { get; set; }
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Connecting;
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.Now;
    public bool UsedAlternativePort { get; init; }
    internal Action? ReleaseResources { init => _releaseResources = value; }

    /// <summary>Encerra o processo e aguarda a confirmação antes de liberar os recursos.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (Process is { HasExited: false })
            {
                Process.Kill(entireProcessTree: true);
                await Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // O processo já foi encerrado ou descartado.
        }
        finally
        {
            ReleaseHeldResources();
        }
    }

    /// <summary>
    /// Encerramento síncrono usado na saída do aplicativo. O sinal de término já foi
    /// enviado ao processo; esperar por ele sem limite travaria a interface, então a
    /// espera é limitada e os recursos são liberados de qualquer forma.
    /// </summary>
    public void Dispose()
    {
        using var timeout = new CancellationTokenSource(ShutdownTimeout);
        try
        {
            StopAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            ReleaseHeldResources();
        }
    }

    private void ReleaseHeldResources()
    {
        Interlocked.Exchange(ref _releaseResources, null)?.Invoke();
        Credentials?.Dispose();
        Process?.Dispose();
    }
}

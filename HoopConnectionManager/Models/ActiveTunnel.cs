using System.Diagnostics;

namespace HoopConnectionManager.Models;

/// <summary>Representa um túnel Hoop ativo mantido em memória.</summary>
public sealed class ActiveTunnel : IDisposable
{
    private Action? _releaseResources;
    private int _disposed;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ConnectionName { get; init; } = string.Empty;
    public Process? Process { get; init; }
    public ConnectionCredentials? Credentials { get; set; }
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Connecting;
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.Now;
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
        catch (InvalidOperationException)
        {
            // O processo já foi encerrado ou descartado.
        }
        finally
        {
            Interlocked.Exchange(ref _releaseResources, null)?.Invoke();
            Credentials?.Dispose();
            Process?.Dispose();
        }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}

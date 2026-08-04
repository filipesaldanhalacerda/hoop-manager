using System.Diagnostics;

namespace HoopConnectionManager.Models;

/// <summary>
/// Representa um túnel Hoop ativo mantido em memória.
/// </summary>
public sealed class ActiveTunnel : IDisposable
{
    private Action? _releaseResources;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ConnectionName { get; init; } = string.Empty;
    public Process? Process { get; init; }
    public ConnectionCredentials? Credentials { get; set; }
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Connecting;
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.Now;
    internal Action? ReleaseResources { init => _releaseResources = value; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _releaseResources, null)?.Invoke();
        Credentials?.Dispose();

        if (Process is { HasExited: false })
        {
            try
            {
                Process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Processo já finalizado.
            }
        }

        Process?.Dispose();
    }
}

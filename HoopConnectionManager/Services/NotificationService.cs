using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação padrão de notificações usando MessageBox do WPF.
/// Em futuras evoluções pode ser substituída por notificações da bandeja.
/// </summary>
public sealed class NotificationService : INotificationService
{
    public event EventHandler<NotificationEventArgs>? NotificationRaised;

    public void Show(string message, NotificationLevel level = NotificationLevel.Information)
    {
        NotificationRaised?.Invoke(this, new NotificationEventArgs(message, level));
    }
}

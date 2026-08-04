namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Nível da notificação ao usuário.
/// </summary>
public enum NotificationLevel
{
    Information,
    Warning,
    Error
}

/// <summary>
/// Serviço responsável por notificar o usuário.
/// Mantém a UI desacoplada dos mecanismos de exibição.
/// </summary>
public interface INotificationService
{
    void Show(string message, NotificationLevel level = NotificationLevel.Information);
    event EventHandler<NotificationEventArgs>? NotificationRaised;
}

public sealed class NotificationEventArgs : EventArgs
{
    public string Message { get; }
    public NotificationLevel Level { get; }

    public NotificationEventArgs(string message, NotificationLevel level)
    {
        Message = message;
        Level = level;
    }
}

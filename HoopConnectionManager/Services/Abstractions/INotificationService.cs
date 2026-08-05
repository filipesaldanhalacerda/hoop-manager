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

public enum NotificationAction
{
    None,
    Reauthenticate,
    OpenSettings,
    SelectDBeaver,
    /// <summary>
    /// Leva à configuração guiada. É a saída correta quando o ambiente sequer chegou
    /// a ser montado — nesse estado, pedir nova autenticação não resolve nada.
    /// </summary>
    OpenGuidedSetup
}

/// <summary>
/// Serviço responsável por notificar o usuário.
/// Mantém a UI desacoplada dos mecanismos de exibição.
/// </summary>
public interface INotificationService
{
    void Show(
        string message,
        NotificationLevel level = NotificationLevel.Information,
        NotificationAction action = NotificationAction.None);
    event EventHandler<NotificationEventArgs>? NotificationRaised;
}

public sealed class NotificationEventArgs : EventArgs
{
    public string Message { get; }
    public NotificationLevel Level { get; }
    public NotificationAction Action { get; }

    public NotificationEventArgs(string message, NotificationLevel level, NotificationAction action = NotificationAction.None)
    {
        Message = message;
        Level = level;
        Action = action;
    }
}

using System.Windows;
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

        var caption = level switch
        {
            NotificationLevel.Warning => "Aviso",
            NotificationLevel.Error => "Erro",
            _ => "Informação"
        };

        var image = level switch
        {
            NotificationLevel.Warning => MessageBoxImage.Warning,
            NotificationLevel.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.Information
        };

        System.Windows.MessageBox.Show(message, caption, System.Windows.MessageBoxButton.OK, image);
    }
}

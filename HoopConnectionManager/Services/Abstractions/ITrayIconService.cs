namespace HoopConnectionManager.Services.Abstractions;

/// <summary>
/// Serviço de ícone da bandeja do Windows.
/// </summary>
public interface ITrayIconService
{
    void Initialize();
    void Show();
    void Hide();
    void ShowBalloonTip(string title, string message);
    event EventHandler? OpenRequested;
    event EventHandler? ExitRequested;
}

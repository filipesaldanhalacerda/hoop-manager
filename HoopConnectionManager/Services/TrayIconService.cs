using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using HoopConnectionManager.Services.Abstractions;
using HoopConnectionManager.Configuration;
using Application = System.Windows.Application;

namespace HoopConnectionManager.Services;

/// <summary>
/// Implementação do ícone da bandeja do Windows.
/// </summary>
public sealed class TrayIconService : ITrayIconService, IDisposable
{
    private readonly IConnectionService _connectionService;
    private readonly ILoggerService _logger;
    private NotifyIcon? _notifyIcon;

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService(IConnectionService connectionService, ILoggerService logger)
    {
        _connectionService = connectionService;
        _logger = logger;
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = ApplicationConstants.ApplicationName,
            Visible = true,
            Icon = SystemIcons.Application
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Abrir", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add("Conectar", null, OnConnectClick);
        contextMenu.Items.Add("Desconectar", null, OnDisconnectClick);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Sair", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon.ContextMenuStrip = contextMenu;
        _logger.LogInformation("Ícone da bandeja inicializado.");
    }

    public void Show()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = true;
        }
    }

    public void Hide()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }
    }

    public void ShowBalloonTip(string title, string message)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
    }

    private void OnConnectClick(object? sender, EventArgs e)
    {
        // Placeholder: abrir janela de seleção de conexão no futuro.
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnDisconnectClick(object? sender, EventArgs e)
    {
        var activeCount = _connectionService.ActiveTunnels.Count;
        if (activeCount == 0)
        {
            ShowBalloonTip(ApplicationConstants.ApplicationName, "Não há conexões ativas para desconectar.");
            return;
        }

        var confirmation = System.Windows.Forms.MessageBox.Show(
            $"Deseja encerrar {activeCount} {(activeCount == 1 ? "conexão ativa" : "conexões ativas")}?",
            "Desconectar todas",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmation == DialogResult.Yes)
        {
            try
            {
                await _connectionService.DisconnectAllAsync();
                ShowBalloonTip(ApplicationConstants.ApplicationName, "Todas as conexões foram encerradas.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao desconectar túneis pela bandeja.");
                ShowBalloonTip("Falha ao desconectar", "Abra o aplicativo para consultar os detalhes.");
            }
        }
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
    }
}

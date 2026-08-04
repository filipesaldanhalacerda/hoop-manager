using System.Diagnostics;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HoopConnectionManager.Tests;

[TestClass]
public sealed class IntegrationScenarioTests
{
    [TestMethod]
    public async Task ReportsHoopDisconnectedWhenCliCannotConfirmSession()
    {
        using var environment = new HoopTestEnvironment(arguments => arguments switch
        {
            "version" => Success("1.51.2"),
            "admin get userinfo" => Failure("unexpected client failure"),
            _ => Failure("failed")
        });

        var connectivity = await environment.Service.GetConnectivityAsync();

        Assert.AreEqual(GlobalConnectivityState.HoopDisconnected, connectivity.State);
    }

    [TestMethod]
    public async Task ReportsExpiredAuthenticationWhenTokenIsRejected()
    {
        using var environment = new HoopTestEnvironment(arguments => arguments switch
        {
            "version" => Success("1.51.2"),
            "admin get userinfo" => Failure("token expired: status 401"),
            _ => Failure("failed")
        });

        var connectivity = await environment.Service.GetConnectivityAsync();

        Assert.AreEqual(GlobalConnectivityState.AuthenticationExpired, connectivity.State);
        StringAssert.Contains(connectivity.Detail, "autenticação expirou");
    }

    [TestMethod]
    public async Task LoadsCatalogFromNewAndLegacyCliOutputs()
    {
        using var modern = new HoopTestEnvironment(arguments => arguments switch
        {
            "version" => Success("1.80.0"),
            "admin get connections --output json" => Success("[{\"name\":\"orders-dev\",\"environment\":\"DEV\",\"type\":\"postgres\"}]"),
            _ => Failure("unexpected")
        });
        var modernConnections = await modern.Service.GetConnectionsAsync();

        using var legacy = new HoopTestEnvironment(arguments => arguments switch
        {
            "version" => Success("1.51.2"),
            "admin get connections --output json" => Failure("unknown flag: --output"),
            "admin get connections" => Success("orders-dev\npayments-stg\nbilling-prd"),
            _ => Failure("unexpected")
        });
        var legacyConnections = await legacy.Service.GetConnectionsAsync();

        Assert.AreEqual(1, modernConnections.Count);
        Assert.AreEqual(3, legacyConnections.Count);
        Assert.AreEqual("PRD", legacyConnections[2].EnvironmentGroup);
    }

    [TestMethod]
    public async Task DetectsUnexpectedProcessExitAndStartsRecovery()
    {
        var history = new RecordingSessionHistory();
        using var connectionService = new ConnectionService(new ExitingProcessHoopService(), new SilentLogger(), history);
        var reconnecting = new TaskCompletionSource<ActiveTunnelsChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        connectionService.ActiveTunnelsChanged += (_, args) =>
        {
            if (args.Status == ConnectionStatus.Reconnecting) reconnecting.TrySetResult(args);
        };

        await connectionService.ConnectAsync("orders-dev");
        var change = await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual("orders-dev", change.ConnectionName);
        Assert.IsTrue(history.EndReasons.Any(reason => reason.Contains("encerrou", StringComparison.OrdinalIgnoreCase)));
        await connectionService.DisconnectAsync("orders-dev");
    }

    [TestMethod]
    public async Task KeepsThreeConnectionsActiveAtTheSameTime()
    {
        using var connectionService = new ConnectionService(new MultiTunnelHoopService(), new SilentLogger(), new RecordingSessionHistory());

        await Task.WhenAll(
            connectionService.ConnectAsync("orders-dev"),
            connectionService.ConnectAsync("payments-stg"),
            connectionService.ConnectAsync("billing-prd"));

        Assert.AreEqual(3, connectionService.ActiveTunnels.Count);
        Assert.AreEqual(3, connectionService.ActiveTunnels.Values.Select(tunnel => tunnel.Credentials!.Port).Distinct().Count());
        await connectionService.DisconnectAllAsync();
    }

    private static CommandResult Success(string output) => new() { ExitCode = 0, StandardOutput = output };
    private static CommandResult Failure(string error) => new() { ExitCode = 1, StandardError = error };

    private sealed class HoopTestEnvironment : IDisposable
    {
        private readonly string _directory;
        public HoopService Service { get; }

        public HoopTestEnvironment(Func<string, CommandResult> handler)
        {
            _directory = Path.Combine(Path.GetTempPath(), "dev-access-integration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var executable = Path.Combine(_directory, ApplicationConstants.HoopExecutableName);
            File.WriteAllBytes(executable, []);
            var settings = new SettingsService(_directory);
            settings.SaveAsync(new ApplicationSettings { HoopExecutablePath = executable }).GetAwaiter().GetResult();
            Service = new HoopService(new ScriptedCommandRunner(handler), settings, new SilentLogger(), () => true);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    private sealed class ScriptedCommandRunner(Func<string, CommandResult> handler) : ICommandRunner
    {
        public Task<CommandResult> RunAsync(string fileName, string arguments, string? workingDirectory = null,
            CancellationToken cancellationToken = default, TimeSpan? timeout = null, IProgress<string>? outputProgress = null) =>
            Task.FromResult(handler(arguments));
    }

    private sealed class ExitingProcessHoopService : MultiTunnelHoopService
    {
        public override Task<ActiveTunnel> ConnectAsync(string name, CancellationToken token = default)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe", Arguments = "/d /c exit 42", UseShellExecute = false, CreateNoWindow = true
            })!;
            return Task.FromResult(CreateTunnel(name, process));
        }
    }

    private class MultiTunnelHoopService : IHoopService
    {
        private int _port = 5500;
        public string? ExecutablePath => "hoop.exe";
        public virtual Task<ActiveTunnel> ConnectAsync(string name, CancellationToken token = default) =>
            Task.FromResult(CreateTunnel(name));
        protected ActiveTunnel CreateTunnel(string name, Process? process = null) => new()
        {
            ConnectionName = name, Process = process, Status = ConnectionStatus.Connected,
            Credentials = new ConnectionCredentials("127.0.0.1", Interlocked.Increment(ref _port), "hoop", "temporary")
        };
        public Task DisconnectAsync(string name) => Task.CompletedTask;
        public Task<GlobalConnectivity> GetConnectivityAsync(CancellationToken token = default) => Task.FromResult(new GlobalConnectivity(GlobalConnectivityState.Online, "OK"));
        public Task<HoopDiagnostics> GetDiagnosticsAsync(CancellationToken token = default) => Task.FromResult(new HoopDiagnostics());
        public Task<IReadOnlyList<Connection>> GetConnectionsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<Connection>>([]);
        public Task<UserSession> GetSessionAsync(CancellationToken token = default) => Task.FromResult(new UserSession { IsAuthenticated = true });
        public Task<bool> IsAuthenticatedAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task<bool> IsInstalledAsync(CancellationToken token = default) => Task.FromResult(true);
    }

    private sealed class RecordingSessionHistory : ISessionHistoryService
    {
        public List<string> EndReasons { get; } = [];
        public event EventHandler? HistoryChanged { add { } remove { } }
        public IReadOnlyList<SessionHistoryEntry> GetEntries() => [];
        public void StartSession(ActiveTunnel tunnel) { }
        public void EndSession(string tunnelId, string reason) => EndReasons.Add(reason);
    }

    private sealed class SilentLogger : ILoggerService
    {
        public event EventHandler<LogEntry>? LogWritten { add { } remove { } }
        public string LogsDirectory => string.Empty;
        public IReadOnlyList<LogEntry> GetRecentEntries(int maximumCount = 500) => [];
        public LogStorageInfo GetStorageInfo() => new(0, 0, null);
        public int ClearOldLogs() => 0;
        public void ApplyRetention() { }
        public void LogInformation(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
        public void LogError(Exception exception, string message) { }
    }
}

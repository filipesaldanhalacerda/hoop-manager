using System.Diagnostics;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HoopConnectionManager.Tests;

[TestClass]
public sealed class HoopParserTests
{
    [TestMethod]
    public void ParsesTextAndGroupsEnvironment()
    {
        var result = HoopOutputParser.ParseConnections("orders-dev DEV postgres\npayments-prd PRD postgres");
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("DEV", result[0].EnvironmentGroup);
        Assert.AreEqual("PRD", result[1].EnvironmentGroup);
    }

    [TestMethod]
    public void InfersEnvironmentFromConnectionNameWhenColumnIsAbsent()
    {
        var result = HoopOutputParser.ParseConnections("orders-dev postgres online\npayments-prd postgres online\nshared-db postgres online");

        Assert.AreEqual("DEV", result[0].EnvironmentGroup);
        Assert.AreEqual("PRD", result[1].EnvironmentGroup);
        Assert.AreEqual("OUTROS", result[2].EnvironmentGroup);
    }

    [TestMethod]
    public void ParsesLegacyCatalogWithOneConnectionNamePerLine()
    {
        var result = HoopOutputParser.ParseConnections("orders-dev\npayments-stg\nbilling-prd");

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("DEV", result[0].EnvironmentGroup);
        Assert.AreEqual("STG", result[1].EnvironmentGroup);
        Assert.AreEqual("PRD", result[2].EnvironmentGroup);
    }

    [TestMethod]
    public void ParsesTemporaryCredentials()
    {
        var result = HoopOutputParser.TryParseCredentials("Host: 127.0.0.1\nPort: 5432\nUsername: hoop\nPassword: temporary");
        Assert.IsNotNull(result);
        Assert.AreEqual(5432, result.Port);
    }
}

[TestClass]
public sealed class SettingsTests
{
    [TestMethod]
    public async Task RoundTripsNonSecretSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hoop-manager-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(directory);
            await service.SaveAsync(new() { Theme = "Dark", FavoriteConnectionIds = ["dev"] });
            var loaded = service.Load();
            Assert.AreEqual("Dark", loaded.Theme);
            CollectionAssert.Contains(loaded.FavoriteConnectionIds, "dev");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>
    /// Load() passou a manter as configurações em memória para não reler o arquivo a
    /// cada consulta; uma gravação feita fora desta instância ainda precisa ser vista.
    /// </summary>
    [TestMethod]
    public async Task ReloadsWhenTheFileChangesOutsideTheInstance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hoop-manager-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var reader = new SettingsService(directory);
            var writer = new SettingsService(directory);

            await writer.SaveAsync(new() { Theme = "Dark" });
            Assert.AreEqual("Dark", reader.Load().Theme);

            await writer.SaveAsync(new() { Theme = "Light" });
            Assert.AreEqual("Light", reader.Load().Theme);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>O cache não pode ser entregue por referência: quem chama pode alterá-lo.</summary>
    [TestMethod]
    public async Task DoesNotLetCallersMutateTheCachedInstance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hoop-manager-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(directory);
            await service.SaveAsync(new() { Theme = "Dark", FavoriteConnectionIds = ["dev"] });

            var first = service.Load();
            first.Theme = "Light";
            first.FavoriteConnectionIds.Add("prd");

            var second = service.Load();
            Assert.AreEqual("Dark", second.Theme);
            CollectionAssert.DoesNotContain(second.FavoriteConnectionIds, "prd");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

[TestClass]
public sealed class CommandRunnerTests
{
    [TestMethod]
    public async Task CapturesStandardOutput()
    {
        var result = await new CommandRunner().RunAsync("cmd.exe", "/d /c echo hoop");
        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.StandardOutput, "hoop");
    }
}

[TestClass]
public sealed class ConnectionServiceTests
{
    [TestMethod]
    public async Task TracksAndDisconnectsTunnel()
    {
        var service = new ConnectionService(new FakeHoopService(), new FakeLogger(), new FakeSessionHistory());
        await service.ConnectAsync("dev");
        Assert.IsTrue(service.IsConnected("dev"));
        await service.DisconnectAsync("dev");
        Assert.IsFalse(service.IsConnected("dev"));
    }

    [TestMethod]
    public async Task TracksMultipleTunnelsAtTheSameTime()
    {
        var service = new ConnectionService(new FakeHoopService(), new FakeLogger(), new FakeSessionHistory());

        var first = await service.ConnectAsync("orders-dev");
        var second = await service.ConnectAsync("payments-prd");

        Assert.AreEqual(2, service.ActiveTunnels.Count);
        Assert.AreNotEqual(first.Credentials!.Port, second.Credentials!.Port);
        Assert.IsTrue(service.IsConnected("orders-dev"));
        Assert.IsTrue(service.IsConnected("payments-prd"));

        await service.DisconnectAllAsync();
    }

    private sealed class FakeHoopService : IHoopService
    {
        private int _nextPort = 5432;
        public string? ExecutablePath => "hoop.exe";
        public Task<ActiveTunnel> ConnectAsync(string name, CancellationToken token = default) => Task.FromResult(new ActiveTunnel
        {
            ConnectionName = name,
            Status = ConnectionStatus.Connected,
            Credentials = new ConnectionCredentials("127.0.0.1", Interlocked.Increment(ref _nextPort), "hoop", "temporary")
        });
        public Task DisconnectAsync(string name) => Task.CompletedTask;
        public Task<GlobalConnectivity> GetConnectivityAsync(CancellationToken token = default) =>
            Task.FromResult(new GlobalConnectivity(GlobalConnectivityState.Online, "OK"));
        public Task<HoopDiagnostics> GetDiagnosticsAsync(CancellationToken token = default) => Task.FromResult(new HoopDiagnostics());
        public Task<IReadOnlyList<Connection>> GetConnectionsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<Connection>>([]);
        public Task<UserSession> GetSessionAsync(CancellationToken token = default) => Task.FromResult(new UserSession());
        public Task<bool> IsAuthenticatedAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task<bool> IsInstalledAsync(CancellationToken token = default) => Task.FromResult(true);
    }
    private sealed class FakeLogger : ILoggerService
    {
        public event EventHandler<LogEntry>? LogWritten { add { } remove { } }
        public string LogsDirectory => string.Empty;
        public IReadOnlyList<LogEntry> GetRecentEntries(int maximumCount = 500) => [];
        public LogStorageInfo GetStorageInfo() => new(0, 0, null);
        public int ClearOldLogs() => 0;
        public void ApplyRetention() { }
        public void LogError(string m) { }
        public void LogError(Exception e, string m) { }
        public void LogInformation(string m) { }
        public void LogWarning(string m) { }
    }

    private sealed class FakeSessionHistory : ISessionHistoryService
    {
        public event EventHandler? HistoryChanged { add { } remove { } }
        public IReadOnlyList<SessionHistoryEntry> GetEntries() => [];
        public void StartSession(ActiveTunnel tunnel) { }
        public void EndSession(string tunnelId, string reason) { }
    }

    [TestMethod]
    public async Task NotifiesConsumersAfterSettingsArePersisted()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hoop-manager-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(directory);
            var notified = false;
            service.SettingsSaved += (_, settings) => notified = settings.RefreshConnectionsOnStartup;

            await service.SaveAsync(new() { RefreshConnectionsOnStartup = true });

            Assert.IsTrue(notified);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task ConcurrentUpdatesDoNotOverwriteEachOther()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hoop-manager-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(directory);
            var updates = Enumerable.Range(1, 20)
                .Select(index => service.UpdateAsync(settings => settings.FavoriteConnectionIds.Add($"db-{index}")));

            await Task.WhenAll(updates);

            Assert.AreEqual(20, service.Load().FavoriteConnectionIds.Distinct().Count());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

[TestClass]
public sealed class CredentialTests
{
    [TestMethod]
    public void DisposeRemovesPasswordFromCredentialObject()
    {
        var credentials = new ConnectionCredentials("127.0.0.1", 5433, "hoop", "temporary-secret");

        credentials.Dispose();

        Assert.AreEqual(string.Empty, credentials.Password);
        Assert.IsFalse(credentials.ToString().Contains("temporary-secret", StringComparison.Ordinal));
    }
}

[TestClass]
public sealed class SessionHistoryTests
{
    [TestMethod]
    public void PersistsSessionMetadataWithoutCredentials()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dev-access-history-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SessionHistoryService(directory);
            var tunnel = new ActiveTunnel
            {
                ConnectionName = "orders-dev",
                Credentials = new ConnectionCredentials("127.0.0.1", 5434, "hoop", "secret-that-must-not-be-saved")
            };

            service.StartSession(tunnel);
            service.EndSession(tunnel.Id, "Desconectado pelo usuário");

            var entry = service.GetEntries().Single();
            Assert.AreEqual("orders-dev", entry.ConnectionName);
            Assert.AreEqual(5434, entry.Port);
            Assert.IsNotNull(entry.EndedAt);
            var persisted = File.ReadAllText(Path.Combine(directory, "session-history.json"));
            Assert.IsFalse(persisted.Contains("secret-that-must-not-be-saved", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

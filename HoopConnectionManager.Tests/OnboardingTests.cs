using HoopConnectionManager.Configuration;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HoopConnectionManager.Tests;

[TestClass]
public sealed class EnvironmentReadinessTests
{
    [TestMethod]
    public void PointsToTheFirstStepThatIsStillMissing()
    {
        Assert.AreEqual(1, new EnvironmentReadiness(false, false, false).FirstPendingStep);
        Assert.AreEqual(2, new EnvironmentReadiness(true, false, false).FirstPendingStep);
        // A etapa 3 é apenas o catálogo e não persiste nada, então é pulada na retomada.
        Assert.AreEqual(4, new EnvironmentReadiness(true, true, false).FirstPendingStep);
        Assert.AreEqual(5, new EnvironmentReadiness(true, true, true).FirstPendingStep);
    }

    [TestMethod]
    public void IsOnlyReadyWhenEveryDependencyIsInPlace()
    {
        Assert.IsTrue(new EnvironmentReadiness(true, true, true).IsReady);
        Assert.IsFalse(new EnvironmentReadiness(true, true, false).IsReady);
        Assert.IsFalse(new EnvironmentReadiness(true, false, true).IsReady);
        Assert.IsFalse(new EnvironmentReadiness(false, true, true).IsReady);
    }

    [TestMethod]
    public void NamesWhatIsMissingInTheSummary()
    {
        var summary = new EnvironmentReadiness(true, true, false).Summary;

        StringAssert.Contains(summary, "DBeaver");
        Assert.IsFalse(summary.Contains("Hoop CLI", StringComparison.Ordinal),
            "O que já está resolvido não deve aparecer como pendência.");
    }
}

[TestClass]
public sealed class FirstRunServiceTests
{
    /// <summary>
    /// Sem o executável não há como uma sessão ser válida; perguntar ao CLI nesse
    /// estado só gastaria um processo para receber uma falha previsível.
    /// </summary>
    [TestMethod]
    public async Task DoesNotClaimAuthenticatedWhenHoopIsMissing()
    {
        using var environment = new FirstRunEnvironment(hoopInstalled: false, hoopAuthenticated: true);

        var readiness = await environment.Service.EvaluateReadinessAsync();

        Assert.IsFalse(readiness.HoopInstalled);
        Assert.IsFalse(readiness.HoopAuthenticated);
        Assert.AreEqual(1, readiness.FirstPendingStep);
    }

    /// <summary>
    /// A verificação roda periodicamente e LocateAsync grava as configurações em disco;
    /// reaproveitar o caminho já salvo evita uma gravação a cada ciclo.
    /// </summary>
    [TestMethod]
    public async Task ReusesTheSavedDBeaverPathWithoutProbingAgain()
    {
        using var environment = new FirstRunEnvironment(hoopInstalled: true, hoopAuthenticated: true, saveDBeaverPath: true);

        var readiness = await environment.Service.EvaluateReadinessAsync();

        Assert.IsTrue(readiness.DBeaverLocated);
        Assert.AreEqual(0, environment.DBeaver.LocateCalls, "O caminho salvo já respondia à pergunta.");
    }

    [TestMethod]
    public async Task FallsBackToDetectionWhenTheSavedPathIsGone()
    {
        using var environment = new FirstRunEnvironment(hoopInstalled: true, hoopAuthenticated: true, saveDBeaverPath: false);
        environment.DBeaver.DetectedPath = "C:\\DBeaver\\dbeaver.exe";

        var readiness = await environment.Service.EvaluateReadinessAsync();

        Assert.IsTrue(readiness.DBeaverLocated);
        Assert.AreEqual(1, environment.DBeaver.LocateCalls);
    }

    [TestMethod]
    public async Task ReportsReadyOnlyWhenEverythingResolves()
    {
        using var environment = new FirstRunEnvironment(hoopInstalled: true, hoopAuthenticated: true, saveDBeaverPath: true);

        var readiness = await environment.Service.EvaluateReadinessAsync();

        Assert.IsTrue(readiness.IsReady);
        Assert.AreEqual(5, readiness.FirstPendingStep);
    }

    private sealed class FirstRunEnvironment : IDisposable
    {
        private readonly string _directory;

        public FirstRunService Service { get; }
        public FakeDBeaverService DBeaver { get; } = new();

        public FirstRunEnvironment(bool hoopInstalled, bool hoopAuthenticated, bool saveDBeaverPath = false)
        {
            _directory = Path.Combine(Path.GetTempPath(), "dev-access-firstrun", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            var settings = new SettingsService(_directory);
            if (saveDBeaverPath)
            {
                var executable = Path.Combine(_directory, "dbeaver.exe");
                File.WriteAllBytes(executable, []);
                settings.SaveAsync(new ApplicationSettings { DBeaverExecutablePath = executable }).GetAwaiter().GetResult();
            }

            Service = new FirstRunService(settings, new FakeHoopService(hoopInstalled, hoopAuthenticated), DBeaver);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    private sealed class FakeDBeaverService : IDBeaverService
    {
        public int LocateCalls { get; private set; }
        public string? DetectedPath { get; set; }

        public Task<string?> LocateAsync(CancellationToken cancellationToken = default)
        {
            LocateCalls++;
            return Task.FromResult(DetectedPath);
        }

        public Task OpenConnectionAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> UpdateConnectionConfigurationAsync(DBeaverConnectionInfo info, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeHoopService(bool installed, bool authenticated) : IHoopService
    {
        public string? ExecutablePath => installed ? "hoop.exe" : null;
        public Task<bool> IsInstalledAsync(CancellationToken token = default) => Task.FromResult(installed);
        public Task<bool> IsAuthenticatedAsync(CancellationToken token = default) => Task.FromResult(authenticated);
        public Task<UserSession> GetSessionAsync(CancellationToken token = default) => Task.FromResult(new UserSession { IsAuthenticated = authenticated });
        public Task<HoopDiagnostics> GetDiagnosticsAsync(CancellationToken token = default) => Task.FromResult(new HoopDiagnostics());
        public Task<GlobalConnectivity> GetConnectivityAsync(CancellationToken token = default) => Task.FromResult(new GlobalConnectivity(GlobalConnectivityState.Online, "OK"));
        public Task<IReadOnlyList<Connection>> GetConnectionsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<Connection>>([]);
        public Task<ActiveTunnel> ConnectAsync(string connectionName, CancellationToken token = default) => throw new NotSupportedException();
        public Task DisconnectAsync(string connectionName) => Task.CompletedTask;
    }
}

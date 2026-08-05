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
        Assert.AreEqual(1, new EnvironmentReadiness(false, false).FirstPendingStep);
        Assert.AreEqual(2, new EnvironmentReadiness(true, false).FirstPendingStep);
        // A etapa 3 é apenas o catálogo e não persiste nada, então é pulada na retomada.
        Assert.AreEqual(4, new EnvironmentReadiness(true, true).FirstPendingStep);
    }

    [TestMethod]
    public void IsOnlyReadyWhenEveryDependencyIsInPlace()
    {
        Assert.IsTrue(new EnvironmentReadiness(true, true).IsReady);
        Assert.IsFalse(new EnvironmentReadiness(true, false).IsReady);
        Assert.IsFalse(new EnvironmentReadiness(false, true).IsReady);
    }

    [TestMethod]
    public void NamesWhatIsMissingInTheSummary()
    {
        var summary = new EnvironmentReadiness(true, false).Summary;

        StringAssert.Contains(summary, "autenticação");
        Assert.IsFalse(summary.Contains("Hoop CLI", StringComparison.Ordinal),
            "O que já está resolvido não deve aparecer como pendência.");
    }

    /// <summary>
    /// O aplicativo não inicia mais nenhum cliente de banco, então a prontidão do
    /// ambiente não pode depender de encontrar um executável na máquina.
    /// </summary>
    [TestMethod]
    public void DoesNotDependOnAnyDatabaseClient()
    {
        var readiness = new EnvironmentReadiness(true, true);

        Assert.IsTrue(readiness.IsReady);
        Assert.AreEqual(0, readiness.PendingItems.Count);
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

    [TestMethod]
    public async Task ReportsPendingAuthenticationWhenTheCliIsInstalledButTheSessionIsNot()
    {
        using var environment = new FirstRunEnvironment(hoopInstalled: true, hoopAuthenticated: false);

        var readiness = await environment.Service.EvaluateReadinessAsync();

        Assert.IsFalse(readiness.IsReady);
        Assert.AreEqual(2, readiness.FirstPendingStep);
    }

    [TestMethod]
    public async Task ReportsReadyOnlyWhenEverythingResolves()
    {
        using var environment = new FirstRunEnvironment(hoopInstalled: true, hoopAuthenticated: true);

        var readiness = await environment.Service.EvaluateReadinessAsync();

        Assert.IsTrue(readiness.IsReady);
        Assert.AreEqual(4, readiness.FirstPendingStep);
    }

    private sealed class FirstRunEnvironment : IDisposable
    {
        private readonly string _directory;

        public FirstRunService Service { get; }

        public FirstRunEnvironment(bool hoopInstalled, bool hoopAuthenticated)
        {
            _directory = Path.Combine(Path.GetTempPath(), "dev-access-firstrun", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            var settings = new SettingsService(_directory);
            settings.SaveAsync(new ApplicationSettings()).GetAwaiter().GetResult();

            Service = new FirstRunService(settings, new FakeHoopService(hoopInstalled, hoopAuthenticated));
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
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

using System.Diagnostics;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Models;
using HoopConnectionManager.Services;
using HoopConnectionManager.Services.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HoopConnectionManager.Tests;

[TestClass]
public sealed class DBeaverServiceTests
{
    /// <summary>
    /// Regressão: quando o DBeaver já estava aberto, o serviço apenas trazia a janela
    /// para frente e devolvia sem nunca enviar -con. A conexão não era criada nem
    /// recebia a porta e a senha novas, e mesmo assim o retorno era de sucesso.
    /// </summary>
    [TestMethod]
    public async Task DeliversConnectionEvenWhenDBeaverIsAlreadyRunning()
    {
        using var environment = new DBeaverTestEnvironment(dbeaverAlreadyRunning: true);

        var delivered = await environment.Service.UpdateConnectionConfigurationAsync(SampleConnection());

        Assert.IsTrue(delivered, "A entrega ao DBeaver deveria ter sido confirmada.");
        Assert.IsNotNull(environment.LastStartInfo, "Nenhum comando foi enviado ao DBeaver.");
        CollectionAssert.Contains(environment.LastStartInfo!.ArgumentList.ToList(), "-con");

        var argument = environment.LastStartInfo.ArgumentList.Last();
        StringAssert.Contains(argument, "host=127.0.0.1");
        StringAssert.Contains(argument, "port=5433");
        StringAssert.Contains(argument, "user=hoop");
        StringAssert.Contains(argument, "password=senha-temporaria");
    }

    [TestMethod]
    public async Task ReusesTheOpenWindowInsteadOfStartingASecondOne()
    {
        using var environment = new DBeaverTestEnvironment(dbeaverAlreadyRunning: true);

        await environment.Service.OpenConnectionAsync(SampleConnection());

        // Um único launcher é iniciado, e ele encerra sozinho após repassar o comando
        // para a janela existente — é assim que nenhuma segunda janela aparece.
        Assert.AreEqual(1, environment.StartCount);
        Assert.IsFalse(environment.LastStartInfo!.ArgumentList.Contains("-reuseWorkspace"),
            "-reuseWorkspace autoriza uma segunda instância e não pode voltar.");
    }

    /// <summary>
    /// O retorno precisa refletir a realidade: antes, um encaminhamento falho ainda
    /// devolvia sucesso e o usuário ficava com credenciais mortas sem nenhum aviso.
    /// </summary>
    [TestMethod]
    public async Task ReportsFailureWhenTheLauncherDoesNotConfirmForwarding()
    {
        using var environment = new DBeaverTestEnvironment(dbeaverAlreadyRunning: true, launcherExitCode: 1);

        var delivered = await environment.Service.UpdateConnectionConfigurationAsync(SampleConnection());

        Assert.IsFalse(delivered);
    }

    [TestMethod]
    public void KeepsTemporaryPasswordOutOfTheSavedConnection()
    {
        var argument = DBeaverService.BuildConnectionArgument(SampleConnection(), exists: true);

        StringAssert.Contains(argument, "savePassword=false");
        StringAssert.Contains(argument, "save=false");
        StringAssert.Contains(argument, "create=false");
    }

    [TestMethod]
    public void CreatesAndSavesTheConnectionOnlyOnTheFirstUse()
    {
        var argument = DBeaverService.BuildConnectionArgument(SampleConnection(), exists: false);

        StringAssert.Contains(argument, "create=true");
        StringAssert.Contains(argument, "save=true");
        StringAssert.Contains(argument, "savePassword=false");
    }

    [TestMethod]
    public void RejectsValuesThatWouldBreakTheArgumentSeparator()
    {
        foreach (var hostile in new[] { "orders|dev", "orders\rdev", "orders\ndev" })
        {
            Assert.ThrowsException<ArgumentException>(
                () => DBeaverService.ValidateConnectionInfo(SampleConnection(connectionName: hostile)),
                $"O valor '{hostile}' deveria ter sido recusado.");
        }
    }

    [TestMethod]
    public void RejectsPortsOutsideTheValidRange()
    {
        foreach (var port in new[] { 0, -1, 65536 })
        {
            Assert.ThrowsException<ArgumentException>(
                () => DBeaverService.ValidateConnectionInfo(SampleConnection(port: port)));
        }
    }

    /// <summary>
    /// Regressão da instalação da Microsoft Store: apontar para o .exe dentro de
    /// WindowsApps ignora a ativação do modelo de aplicativo e cada chamada vira uma
    /// instância isolada — foi o que abriu a segunda janela na máquina corporativa.
    /// </summary>
    [TestMethod]
    public void UsesTheExecutionAliasForPackagedInstalls()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dev-access-alias", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var alias = Path.Combine(directory, "dbeaver.exe");
            File.WriteAllBytes(alias, []);
            const string packaged = @"C:\Program Files\WindowsApps\DBeaverCorp.DBeaverCE_26.1.3.0_x64__1b7tdvn0p0f9y\dbeaver.exe";

            Assert.AreEqual(alias, DBeaverService.GetCommandLineExecutable(packaged, directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void KeepsThePackagedPathWhenNoAliasIsRegistered()
    {
        var empty = Path.Combine(Path.GetTempPath(), "dev-access-alias", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            const string packaged = @"C:\Program Files\WindowsApps\DBeaverCorp.DBeaverCE_26.1.3.0_x64__1b7tdvn0p0f9y\dbeaver.exe";

            Assert.AreEqual(packaged, DBeaverService.GetCommandLineExecutable(packaged, empty));
        }
        finally { Directory.Delete(empty, true); }
    }

    /// <summary>
    /// Instalação tradicional não pode ser afetada pelo tratamento do caso empacotado.
    /// </summary>
    [TestMethod]
    public void LeavesRegularInstallsUntouched()
    {
        const string regular = @"C:\Program Files\DBeaver\dbeaver.exe";

        Assert.AreEqual(regular, DBeaverService.GetCommandLineExecutable(regular));
    }

    private static DBeaverConnectionInfo SampleConnection(string? connectionName = null, int port = 5433) => new()
    {
        ConnectionId = "orders-dev",
        ConnectionName = connectionName ?? "orders-dev",
        Host = "127.0.0.1",
        Port = port,
        Username = "hoop",
        Password = "senha-temporaria"
    };

    private sealed class DBeaverTestEnvironment : IDisposable
    {
        private readonly string _directory;

        public DBeaverService Service { get; }
        public ProcessStartInfo? LastStartInfo { get; private set; }
        public int StartCount { get; private set; }

        public DBeaverTestEnvironment(bool dbeaverAlreadyRunning, int launcherExitCode = 0)
        {
            _directory = Path.Combine(Path.GetTempPath(), "dev-access-dbeaver", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var executable = Path.Combine(_directory, "dbeaver.exe");
            File.WriteAllBytes(executable, []);

            var settings = new SettingsService(_directory);
            settings.SaveAsync(new ApplicationSettings { DBeaverExecutablePath = executable }).GetAwaiter().GetResult();

            Service = new DBeaverService(
                settings,
                new SilentLogger(),
                () => dbeaverAlreadyRunning,
                startInfo =>
                {
                    LastStartInfo = startInfo;
                    StartCount++;
                    // Um launcher real repassa o comando à instância aberta e encerra;
                    // este processo curto reproduz exatamente esse ciclo de vida.
                    return Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/d /c exit {launcherExitCode}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                });
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
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

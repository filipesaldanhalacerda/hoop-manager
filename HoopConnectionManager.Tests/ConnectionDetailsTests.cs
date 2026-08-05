using HoopConnectionManager.Configuration;
using HoopConnectionManager.Models;
using HoopConnectionManager.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HoopConnectionManager.Tests;

[TestClass]
public sealed class ConnectionDetailsTests
{
    [TestMethod]
    public void BuildsTheJdbcUrlFromTheActiveTunnel()
    {
        var connection = Connected(5434);

        Assert.AreEqual("jdbc:postgresql://127.0.0.1:5434/postgres", connection.JdbcUrl);
    }

    /// <summary>
    /// Sem túnel não há porta nem senha: mostrar um endereço montado daria a impressão
    /// de que existe algo para conectar.
    /// </summary>
    [TestMethod]
    public void DoesNotInventAnEndpointBeforeTheTunnelExists()
    {
        var connection = new ConnectionViewModel(new Connection { Name = "orders-dev" });

        Assert.IsFalse(connection.HasCredentials);
        Assert.AreEqual("Aguardando túnel", connection.JdbcUrl);
        Assert.AreEqual(string.Empty, connection.ConnectionSummary);
    }

    [TestMethod]
    public void SummaryCarriesEveryFieldNeededToCreateTheConnectionByHand()
    {
        var summary = Connected(5434).ConnectionSummary;

        StringAssert.Contains(summary, "Host: 127.0.0.1");
        StringAssert.Contains(summary, "Porta: 5434");
        StringAssert.Contains(summary, $"Database: {ApplicationConstants.DefaultDatabaseName}");
        StringAssert.Contains(summary, "Usuário: noop");
        StringAssert.Contains(summary, "Senha: temporaria");
        StringAssert.Contains(summary, "URL: jdbc:postgresql://127.0.0.1:5434/postgres");
    }

    /// <summary>
    /// A porta muda a cada túnel; os campos derivados precisam acompanhar, senão o
    /// painel mostraria o endereço do túnel anterior.
    /// </summary>
    [TestMethod]
    public void RefreshesDerivedFieldsWhenTheTunnelIsRecreated()
    {
        var connection = Connected(5433);
        var changed = new List<string>();
        connection.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        connection.SetCredentials(new ConnectionCredentials("127.0.0.1", 5434, "noop", "outra"));

        Assert.AreEqual("jdbc:postgresql://127.0.0.1:5434/postgres", connection.JdbcUrl);
        StringAssert.Contains(connection.ConnectionSummary, "Porta: 5434");
        CollectionAssert.Contains(changed, nameof(ConnectionViewModel.JdbcUrl));
        CollectionAssert.Contains(changed, nameof(ConnectionViewModel.ConnectionSummary));
    }

    [TestMethod]
    public void KeepsThePasswordHiddenUntilAskedFor()
    {
        var connection = Connected(5433);

        Assert.AreNotEqual("temporaria", connection.PasswordDisplay);

        connection.TogglePasswordVisibilityCommand.Execute(null);

        Assert.AreEqual("temporaria", connection.PasswordDisplay);
    }

    private static ConnectionViewModel Connected(int port)
    {
        var connection = new ConnectionViewModel(new Connection
        {
            Name = "orders-dev",
            Environment = EnvironmentType.Development
        });
        connection.SetCredentials(new ConnectionCredentials("127.0.0.1", port, "noop", "temporaria"));
        return connection;
    }
}

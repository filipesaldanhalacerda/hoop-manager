using HoopConnectionManager.Helpers;
using HoopConnectionManager.Models;
using HoopConnectionManager.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HoopConnectionManager.Tests;

[TestClass]
public sealed class ConnectionSearchTests
{
    /// <summary>
    /// Regressão: a busca comparava apenas a sigla do ambiente. "PRD" não contém "prod",
    /// então nem a palavra inteira nem a abreviação encontravam a conexão de produção.
    /// </summary>
    [TestMethod]
    public void FindsTheEnvironmentByWordAndNotOnlyByCode()
    {
        var production = Connection("billing-prd", EnvironmentType.Production);

        foreach (var term in new[] { "PRD", "prd", "prod", "produção", "producao", "production" })
        {
            Assert.IsTrue(ConnectionSearch.Matches(production, term), $"'{term}' deveria encontrar produção.");
        }
    }

    [TestMethod]
    public void FindsDevelopmentAndStagingByTheirUsualWords()
    {
        var development = Connection("orders-dev", EnvironmentType.Development);
        var staging = Connection("orders-stg", EnvironmentType.Staging);

        Assert.IsTrue(ConnectionSearch.Matches(development, "desenvolvimento"));
        Assert.IsTrue(ConnectionSearch.Matches(staging, "homologação"));
        Assert.IsTrue(ConnectionSearch.Matches(staging, "hml"));
        Assert.IsFalse(ConnectionSearch.Matches(development, "produção"),
            "Sinônimos de um ambiente não podem vazar para outro.");
    }

    /// <summary>
    /// Regressão: OrdinalIgnoreCase resolve caixa, mas não acentuação.
    /// </summary>
    [TestMethod]
    public void IgnoresAccentsInBothDirections()
    {
        var staging = Connection("orders-stg", EnvironmentType.Staging);

        Assert.IsTrue(ConnectionSearch.Matches(staging, "homologacao"));
        Assert.IsTrue(ConnectionSearch.Matches(staging, "HOMOLOGAÇÃO"));
    }

    /// <summary>
    /// Regressão: a busca era um único Contains, então dois termos nunca casavam.
    /// </summary>
    [TestMethod]
    public void RequiresEveryTermToMatch()
    {
        var orders = Connection("orders-dev", EnvironmentType.Development);
        var payments = Connection("payments-dev", EnvironmentType.Development);

        Assert.IsTrue(ConnectionSearch.Matches(orders, "dev orders"));
        Assert.IsTrue(ConnectionSearch.Matches(orders, "orders   dev"));
        Assert.IsFalse(ConnectionSearch.Matches(payments, "orders dev"),
            "Um termo que não aparece precisa excluir o resultado.");
    }

    [TestMethod]
    public void FindsByStatusLabel()
    {
        var connection = Connection("orders-dev", EnvironmentType.Development);
        connection.Status = ConnectionStatus.Connected;

        Assert.IsTrue(ConnectionSearch.Matches(connection, "conectado"));
        Assert.IsFalse(ConnectionSearch.Matches(connection, "erro"));
    }

    [TestMethod]
    public void FindsByHostAndPortOnceTheTunnelIsUp()
    {
        var connection = Connection("orders-dev", EnvironmentType.Development);
        connection.SetCredentials(new ConnectionCredentials("127.0.0.1", 5434, "hoop", "temporaria"));

        Assert.IsTrue(ConnectionSearch.Matches(connection, "5434"));
        Assert.IsTrue(ConnectionSearch.Matches(connection, "127.0.0.1"));
        Assert.IsFalse(ConnectionSearch.Matches(connection, "5433"));
    }

    [TestMethod]
    public void FindsByTypeWhenTheCatalogProvidesIt()
    {
        var connection = new ConnectionViewModel(new Connection
        {
            Name = "orders-dev",
            Environment = EnvironmentType.Development,
            Type = "postgres"
        });

        Assert.IsTrue(ConnectionSearch.Matches(connection, "postgres"));
    }

    [TestMethod]
    public void ShowsEverythingWhenTheQueryIsEmpty()
    {
        var connection = Connection("orders-dev", EnvironmentType.Development);

        Assert.IsTrue(ConnectionSearch.Matches(connection, null));
        Assert.IsTrue(ConnectionSearch.Matches(connection, string.Empty));
        Assert.IsTrue(ConnectionSearch.Matches(connection, "   "));
    }

    private static ConnectionViewModel Connection(string name, EnvironmentType environment) =>
        new(new Connection { Name = name, Environment = environment });
}

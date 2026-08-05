using HoopConnectionManager.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HoopConnectionManager.Tests;

[TestClass]
public sealed class HoopConnectionNameTests
{
    [TestMethod]
    public void ReadsTheCorporateNamingPattern()
    {
        Assert.IsTrue(HoopConnectionName.TryParse("techsaz-dev-postgres-corafoundation002-iboundbra-rw", out var parsed));

        Assert.AreEqual("techsaz", parsed.Technology);
        Assert.AreEqual("dev", parsed.Environment);
        Assert.AreEqual("postgres", parsed.Engine);
        Assert.AreEqual("corafoundation002", parsed.Team);
        Assert.AreEqual("iboundbra", parsed.Database);
        Assert.AreEqual("rw", parsed.Access);
    }

    /// <summary>
    /// O nome completo passa de cinquenta caracteres e a parte que identifica o banco
    /// fica no meio — exatamente onde as listas truncam.
    /// </summary>
    [TestMethod]
    public void BuildsAShortLabelThatIdentifiesTheDatabaseAtAGlance()
    {
        HoopConnectionName.TryParse("techsaz-dev-postgres-corafoundation002-iboundbra-rw", out var parsed);

        Assert.AreEqual("iboundbra-dev-rw", parsed.ShortLabel);
    }

    /// <summary>
    /// Ancorar pelas duas pontas permite que o nome do banco tenha hífen.
    /// </summary>
    [TestMethod]
    public void KeepsHyphensThatBelongToTheDatabaseName()
    {
        Assert.IsTrue(HoopConnectionName.TryParse("techsaz-prd-postgres-corafoundation002-inbound-arg-rw", out var parsed));

        Assert.AreEqual("inbound-arg", parsed.Database);
        Assert.AreEqual("inbound-arg-prd-rw", parsed.ShortLabel);
    }

    /// <summary>
    /// Quando o último segmento não é uma permissão conhecida, ele pertence ao nome do
    /// banco — melhor manter o nome inteiro do que descartar um pedaço dele.
    /// </summary>
    [TestMethod]
    public void TreatsAnUnknownLastSegmentAsPartOfTheDatabaseName()
    {
        Assert.IsTrue(HoopConnectionName.TryParse("techsaz-hml-postgres-corafoundation002-billing-legacy", out var parsed));

        Assert.AreEqual("billing-legacy", parsed.Database);
        Assert.AreEqual(string.Empty, parsed.Access);
        Assert.AreEqual("billing-legacy-hml", parsed.ShortLabel);
    }

    /// <summary>
    /// Fora do padrão, o nome original precisa sobreviver intacto: inventar significado
    /// produziria rótulos errados em conexões que não seguem a convenção.
    /// </summary>
    [TestMethod]
    public void RefusesNamesOutsideThePattern()
    {
        // O último é um nome curto demais: sem os seis segmentos não há como saber
        // qual pedaço é o time e qual é o banco.
        foreach (var name in new[]
                 {
                     "orders-dev", "meu_banco", "", "   ",
                     "techsaz-xyz-postgres-time-banco-rw",
                     "techsaz-hml-postgres-corafoundation002-billing"
                 })
        {
            Assert.IsFalse(HoopConnectionName.TryParse(name, out _), $"'{name}' não deveria ser interpretado.");
        }
    }

    [TestMethod]
    public void ConnectionFallsBackToTheFullNameWhenThePatternDoesNotApply()
    {
        var corporate = new Connection { Name = "techsaz-dev-postgres-corafoundation002-iboundbra-rw" };
        var other = new Connection { Name = "orders-dev" };
        var withFriendlyName = new Connection { Name = "techsaz-dev-postgres-corafoundation002-iboundbra-rw", FriendlyName = "Inbound Brasil" };

        Assert.AreEqual("iboundbra-dev-rw", corporate.DisplayName);
        Assert.AreEqual("orders-dev", other.DisplayName);
        Assert.AreEqual("Inbound Brasil", withFriendlyName.DisplayName,
            "O apelido do catálogo tem prioridade sobre o rótulo derivado.");
    }
}

using System.Reflection;
using HoopConnectionManager.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HoopConnectionManager.Tests;

/// <summary>
/// O script de instalação vive como recurso embutido e é referenciado por nome. Um
/// rename de pasta ou a perda da entrada no .csproj passariam despercebidos até alguém
/// tentar instalar numa máquina sem Hoop, que é o pior momento para descobrir.
/// </summary>
[TestClass]
public sealed class BundledInstallerTests
{
    [TestMethod]
    public void ShipsTheOfficialInstallScriptAsAnEmbeddedResource()
    {
        using var stream = ScriptStream();

        Assert.IsNotNull(stream, $"O recurso '{InstallerService.BundledScriptResource}' não está embutido no aplicativo.");
    }

    [TestMethod]
    public void KeepsTheStepsThatTheApplicationDependsOn()
    {
        using var stream = ScriptStream()!;
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();

        // A detecção do aplicativo procura em %UserProfile%\hoop e relê o PATH do usuário:
        // se o script deixar de fazer qualquer um dos dois, a instalação "conclui" e o
        // Hoop continua invisível para o aplicativo.
        StringAssert.Contains(script, "Join-Path $HOME \"hoop\"");
        StringAssert.Contains(script, "SetEnvironmentVariable(\"PATH\"");
        StringAssert.Contains(script, "releases.hoop.dev");
        StringAssert.Contains(script, "exit 1", "O instalador precisa sinalizar falha pelo código de saída.");
    }

    private static Stream? ScriptStream() =>
        typeof(InstallerService).Assembly.GetManifestResourceStream(InstallerService.BundledScriptResource);
}

using System.Reflection;

namespace HoopConnectionManager.Helpers;

public static class ApplicationVersion
{
    public static string Current
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(ApplicationVersion).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString(3);
            return string.IsNullOrWhiteSpace(version) ? "Não identificada" : version;
        }
    }
}

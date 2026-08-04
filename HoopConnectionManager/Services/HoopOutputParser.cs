using System.Text.Json;
using System.Text.RegularExpressions;
using HoopConnectionManager.Models;
using HoopConnectionManager.Models.Hoop;

namespace HoopConnectionManager.Services;

/// <summary>
/// Interpreta saídas do Hoop CLI e converte em objetos do domínio.
/// Implementação preparada para múltiplos formatos (texto e JSON).
/// </summary>
public static class HoopOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<Connection> ParseConnections(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var trimmed = output.Trim();

        if (trimmed.StartsWith('['))
        {
            return ParseConnectionsJson(trimmed);
        }

        if (trimmed.StartsWith('{'))
        {
            return ParseConnectionsJsonObject(trimmed);
        }

        return ParseConnectionsText(trimmed);
    }

    private static IReadOnlyList<Connection> ParseConnectionsJson(string json)
    {
        try
        {
            var dtos = JsonSerializer.Deserialize<List<HoopConnectionDto>>(json, JsonOptions);
            if (dtos is null)
            {
                return [];
            }

            return dtos.Select(Map).Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<Connection> ParseConnectionsJsonObject(string json)
    {
        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, List<HoopConnectionDto>>>(json, JsonOptions);
            if (root is null)
            {
                return [];
            }

            return root.Values.SelectMany(MapMany).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<Connection> ParseConnectionsText(string output)
    {
        var connections = new List<Connection>();
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var connection = ParseConnectionLine(line);
            if (connection is not null)
            {
                connections.Add(connection);
            }
        }

        return connections;
    }

    private static IEnumerable<Connection> MapMany(IEnumerable<HoopConnectionDto> dtos)
    {
        return dtos.Select(Map).Where(c => !string.IsNullOrWhiteSpace(c.Name));
    }

    private static Connection Map(HoopConnectionDto dto)
    {
        return new Connection
        {
            Name = dto.Name ?? string.Empty,
            FriendlyName = dto.FriendlyName,
            Environment = ParseEnvironment(dto.Environment, dto.Name),
            Type = dto.Type ?? string.Empty
        };
    }

    private static Connection? ParseConnectionLine(string line)
    {
        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var name = parts[0].Trim();
        var environment = ParseEnvironment(parts[1], name);

        return new Connection
        {
            Name = name,
            Environment = environment,
            Type = parts.Length > 2 ? parts[2] : string.Empty
        };
    }

    public static async Task<ConnectionCredentials?> WaitForCredentialsAsync(
        Task<string> outputTask,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            var output = await outputTask.WaitAsync(cts.Token);
            return TryParseCredentials(output);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public static ConnectionCredentials? TryParseCredentials(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var trimmed = output.Trim();
        if (trimmed.StartsWith('{'))
        {
            return TryParseCredentialsJson(trimmed);
        }

        var host = ExtractValue(output, @"Host[:\s=]+([\w\.\-]+)") ?? "127.0.0.1";
        var portText = ExtractValue(output, @"Port[:\s=]+(\d+)");
        var user = ExtractValue(output, @"User(?:name)?[:\s=]+(\S+)") ?? "hoop";
        var password = ExtractValue(output, @"Password[:\s=]+(\S+)");

        if (!int.TryParse(portText, out var port) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new ConnectionCredentials(host, port, user, password);
    }

    private static ConnectionCredentials? TryParseCredentialsJson(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<HoopCredentialsDto>(json, JsonOptions);
            if (dto?.Host is null || dto.Port is null || dto.Username is null || dto.Password is null)
            {
                return null;
            }

            return new ConnectionCredentials(dto.Host, dto.Port.Value, dto.Username, dto.Password);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static EnvironmentType ParseEnvironment(string? environment, string? connectionName = null)
    {
        var parsed = (environment ?? string.Empty).ToUpperInvariant() switch
        {
            "DEV" or "DEVELOPMENT" => EnvironmentType.Development,
            "STG" or "STAGING" or "HML" or "HOMOLOG" or "HOMOLOGATION" or "QA" => EnvironmentType.Staging,
            "PRD" or "PRODUCTION" => EnvironmentType.Production,
            _ => EnvironmentType.Unknown
        };

        if (parsed != EnvironmentType.Unknown || string.IsNullOrWhiteSpace(connectionName))
        {
            return parsed;
        }

        var normalizedName = connectionName.ToUpperInvariant();
        if (Regex.IsMatch(normalizedName, @"(^|[^A-Z0-9])(DEV|DEVELOPMENT)([^A-Z0-9]|$)"))
        {
            return EnvironmentType.Development;
        }

        if (Regex.IsMatch(normalizedName, @"(^|[^A-Z0-9])(STG|STAGING|HML|HOMOLOG|HOMOLOGATION|QA)([^A-Z0-9]|$)"))
        {
            return EnvironmentType.Staging;
        }

        if (Regex.IsMatch(normalizedName, @"(^|[^A-Z0-9])(PRD|PROD|PRODUCTION)([^A-Z0-9]|$)"))
        {
            return EnvironmentType.Production;
        }

        return EnvironmentType.Unknown;
    }

    private static string? ExtractValue(string input, string pattern)
    {
        var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }
}

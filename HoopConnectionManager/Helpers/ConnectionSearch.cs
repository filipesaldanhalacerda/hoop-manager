using System.Globalization;
using System.Text;
using HoopConnectionManager.ViewModels;

namespace HoopConnectionManager.Helpers;

/// <summary>
/// Regra de busca do catálogo de conexões. Fica fora do ViewModel para poder ser testada
/// e para manter num único lugar aquilo que a caixa de busca promete encontrar.
/// </summary>
public static class ConnectionSearch
{
    /// <summary>
    /// Termos aceitos por ambiente. A tela mostra a sigla, mas quem busca digita a palavra:
    /// "produção" não encontrava nada porque "PRD" não contém "prod".
    /// </summary>
    private static readonly Dictionary<string, string[]> EnvironmentTerms = new(StringComparer.Ordinal)
    {
        ["DEV"] = ["dev", "desenvolvimento", "development"],
        ["STG"] = ["stg", "staging", "hml", "homologacao", "homologation", "qa", "teste"],
        ["PRD"] = ["prd", "prod", "producao", "production"],
        ["OUTROS"] = ["outros", "sem ambiente", "desconhecido"]
    };

    public static bool Matches(ConnectionViewModel connection, string? query)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var searchable = BuildSearchableText(connection);

        // Todos os termos precisam aparecer: quem digita "dev orders" espera as duas coisas,
        // e não qualquer conexão que satisfaça uma delas.
        return terms.All(term => searchable.Contains(Normalize(term), StringComparison.Ordinal));
    }

    private static string BuildSearchableText(ConnectionViewModel connection)
    {
        var builder = new StringBuilder();

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.Append(Normalize(value)).Append(' ');
            }
        }

        Add(connection.DisplayName);
        Add(connection.Name);
        Add(connection.EnvironmentGroup);
        if (EnvironmentTerms.TryGetValue(connection.EnvironmentGroup, out var synonyms))
        {
            foreach (var synonym in synonyms)
            {
                Add(synonym);
            }
        }

        Add(connection.Type);
        Add(connection.ConnectionStateLabel);
        Add(connection.Host);
        Add(connection.Port?.ToString(CultureInfo.InvariantCulture));
        Add(connection.LocalEndpoint);

        return builder.ToString();
    }

    /// <summary>
    /// Tira acentuação e caixa. Sem isso, "homologacao" não encontrava "homologação" —
    /// OrdinalIgnoreCase resolve maiúsculas, mas não diacríticos.
    /// </summary>
    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

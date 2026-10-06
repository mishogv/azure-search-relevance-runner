using System.Text;
using Azure.Search.Documents.Models;

namespace SearchFix.Runner;

public interface IQueryBuilder
{
    string Name { get; }
    BuiltQuery Build(string input);
}

/// <summary>
/// The production code, verbatim from the developer brief (EncodeSpecialCharacters + the
/// SearchMode resolution in SearchInternalAsync). Used only to reproduce the baseline.
/// </summary>
public sealed class CurrentQueryBuilder : IQueryBuilder
{
    public string Name => "current";

    public BuiltQuery Build(string input)
    {
        var searchText = string.IsNullOrWhiteSpace(input) ? "*" : EncodeSpecialCharacters(input);
        var (mode, text) = ResolveMode(searchText, SearchMode.Any);
        return new BuiltQuery(text, mode, Array.Empty<string>());
    }

    public static string EncodeSpecialCharacters(string searchText)
    {
        var stringBuilder = new StringBuilder();
        foreach (var c in searchText)
        {
            if ("+&|!\"()[]{}^~:'\\/".Contains(c.ToString()))
                stringBuilder.Append("\\" + c.ToString());
            else
                stringBuilder.Append(c);
        }
        // Wrap in quotes if query contains a dash, then wrap in regex wildcard OR
        var encodedSearchText = searchText.Contains('-')
            ? "\"" + stringBuilder.ToString() + "\""
            : stringBuilder.ToString();
        return !string.IsNullOrEmpty(encodedSearchText)
            ? string.Format("/.*{0}.*/|{0}", encodedSearchText)
            : string.Empty;
    }

    /// <summary>Existing OR/AND keyword detection from SearchInternalAsync — kept as-is in every variant.</summary>
    public static (SearchMode Mode, string Text) ResolveMode(string searchText, SearchMode requested)
    {
        var searchMode = requested;
        if (searchMode == SearchMode.All &&
            (searchText.StartsWith("OR ", StringComparison.Ordinal) || searchText.Contains(" OR ", StringComparison.Ordinal)))
        {
            searchMode = SearchMode.Any;
            searchText = searchText.Replace("OR ", string.Empty, StringComparison.Ordinal);
        }
        else if (searchMode == SearchMode.Any &&
            (searchText.StartsWith("AND ", StringComparison.Ordinal) || searchText.Contains(" AND ", StringComparison.Ordinal)))
        {
            searchMode = SearchMode.All;
            searchText = searchText.Replace("AND ", string.Empty, StringComparison.Ordinal);
        }
        return (searchMode, searchText);
    }
}

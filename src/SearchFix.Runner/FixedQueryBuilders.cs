using System.Text;
using Azure.Search.Documents.Models;

namespace SearchFix.Runner;

/// <summary>
/// Step 2: query-side fix only. Default SearchMode.All, OR/AND keyword detection kept,
/// EncodeSpecialCharacters replaced by predictable term handling (no regex, no OR wrapper).
/// </summary>
public sealed class Step2QueryBuilder : IQueryBuilder
{
    public string Name => "step2";

    public BuiltQuery Build(string input)
    {
        var encoded = string.IsNullOrWhiteSpace(input) ? "" : Encode(input);
        var text = encoded.Length == 0 ? "*" : encoded;   // empty, or nothing left after stripping quotes
        var (mode, resolved) = CurrentQueryBuilder.ResolveMode(text, SearchMode.All);
        return new BuiltQuery(resolved, mode, Array.Empty<string>());
    }

    // Full Lucene specials. '-' is handled separately: it is an operator only at the start of a term.
    private const string Specials = "+&|!(){}[]^~*?:\\/";

    /// <summary>Zero-width spaces (U+\u200B) appear in the data and in six queries; they carry no meaning.</summary>
    public static string Clean(string input) => input.Replace("\u200B", "").Trim();

    /// <summary>
    /// Decision table:
    ///   balanced quotes   → phrases kept, specials inside and outside escaped, quotes untouched
    ///   unbalanced quote  → quotes removed, then treated as plain text
    ///   plain text        → specials escaped; a term-leading '-' or '!' escaped so it is not a NOT
    /// </summary>
    public static string Encode(string input)
    {
        var s = Clean(input);
        var quotes = s.Count(c => c == '"');
        if (quotes % 2 == 1) s = s.Replace("\"", "");

        var sb = new StringBuilder(s.Length + 8);
        var termStart = true;
        foreach (var c in s)
        {
            if (c == '"') { sb.Append(c); termStart = true; continue; }
            if (char.IsWhiteSpace(c)) { sb.Append(c); termStart = true; continue; }
            if (Specials.Contains(c) || (termStart && (c == '-' || c == '!'))) sb.Append('\\');
            sb.Append(c);
            termStart = false;
        }
        return sb.ToString();
    }
}

/// <summary>
/// Step 3: identifier evidence first, free text second.
///   single token  → exact^10 OR normalized^5 OR prefix^2 (prefix only from 4 chars) OR the standard field
///   several tokens → whole input as exact phrase, every identifier-like token (contains a digit) as an
///                    exact/normalized hit, OR all terms on the standard field (SearchMode.All)
/// A partial identifier plus words that are not in the document therefore does not match (MS-76),
/// while a complete identifier plus extra words still does (MF-4).
/// </summary>
public sealed class Step3QueryBuilder : IQueryBuilder
{
    public string Name => "step3";
    public const int MinPrefixLength = 4;
    public const int MaxTypeCodeLength = 3;

    public BuiltQuery Build(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new BuiltQuery("*", SearchMode.All, Array.Empty<string>(), Indexes.ScoringProfile);

        var escaped = Step2QueryBuilder.Encode(input);
        var (mode, free) = CurrentQueryBuilder.ResolveMode(escaped.Length == 0 ? "*" : escaped, SearchMode.All);

        // Identifier clauses take the raw text; the field analyzers do the normalizing.
        var phrase = Step2QueryBuilder.Clean(input).Replace("\\", "").Replace("\"", "");
        var tokens = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return new BuiltQuery(free, mode, Array.Empty<string>(), Indexes.ScoringProfile);

        // A bare type code ("RCP", "DOC") is identifier evidence only: it must not fan out over every
        // record of that type through the standard field. Only a document that IS that code matches.
        if (tokens.Length == 1 && tokens[0].Length <= MaxTypeCodeLength && tokens[0].All(char.IsLetter))
            return new BuiltQuery($"{Indexes.ExactField}:\"{phrase}\"", mode, Array.Empty<string>(), Indexes.ScoringProfile);

        var clauses = new List<string>
        {
            $"{Indexes.ExactField}:\"{phrase}\"^10",
            $"{Indexes.NormField}:\"{phrase}\"^5",      // whole input, separators ignored (also for document names)
        };
        if (tokens.Length == 1)
        {
            if (Normalize(phrase).Length >= MinPrefixLength) clauses.Add($"{Indexes.PrefixField}:\"{phrase}\"^2");
        }
        else
        {
            foreach (var t in tokens.Where(t => t.Any(char.IsDigit)))
                clauses.Add($"{Indexes.ExactField}:\"{t}\"^8 OR {Indexes.NormField}:\"{t}\"^4");
        }
        clauses.Add($"content:({free})");
        return new BuiltQuery(string.Join(" OR ", clauses), mode, Array.Empty<string>(), Indexes.ScoringProfile);
    }

    /// <summary>Same normalization as the index-side char filter: lowercase, keep letters and digits only.</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}

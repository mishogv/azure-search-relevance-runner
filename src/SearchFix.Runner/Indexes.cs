using Azure.Search.Documents.Indexes.Models;

namespace SearchFix.Runner;

public static class Indexes
{
    /// <summary>Baseline index: one standard-analyzed, retrievable content field, as the brief prescribes.</summary>
    public static SearchIndex Baseline(string name) => new(name)
    {
        Fields =
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
            new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.StandardLucene },
        },
    };

    public const string ExactField = "content_exact";
    public const string NormField = "content_norm";
    public const string PrefixField = "content_prefix";
    public const string ScoringProfile = "identifiers";

    /// <summary>
    /// Step 3 index: the standard field plus three identifier fields fed from the same string.
    ///  - exact:  keyword + lowercase + trim                    → "part-0033" as one token
    ///  - norm:   separators stripped + keyword + lowercase     → "part0033"
    ///  - prefix: norm + edge n-grams (index side only)         → "pa", "par", … ; searched with the norm analyzer
    /// Separate top-level fields: Azure AI Search has no sub-fields on string fields.
    /// </summary>
    public static SearchIndex Step3(string name)
    {
        const string normalizer = "separator_normalizer";
        const string edge = "prefix_edge_4_30";   // min 4: "ITM7" must match, a bare type code "RCP" must not
        const string exactAnalyzer = "identifier_exact";
        const string normAnalyzer = "identifier_normalized";
        const string prefixAnalyzer = "identifier_prefix";

        var index = new SearchIndex(name)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.StandardLucene },
                new SearchableField(ExactField) { AnalyzerName = exactAnalyzer, IsHidden = true },
                new SearchableField(NormField) { AnalyzerName = normAnalyzer, IsHidden = true },
                new SearchableField(PrefixField) { IndexAnalyzerName = prefixAnalyzer, SearchAnalyzerName = normAnalyzer, IsHidden = true },
            },
            // Everything that is not a letter or a digit is deleted before tokenizing: "-", "_", ".", "#",
            // spaces and the zero-width space (U+200B). "NC-4421-B_2#10.H" and "NC4421B210H" become one token.
            CharFilters = { new PatternReplaceCharFilter(normalizer, "[^\\p{L}\\p{N}]", "") },
            TokenFilters = { new EdgeNGramTokenFilter(edge) { MinGram = Step3QueryBuilder.MinPrefixLength, MaxGram = 30, Side = EdgeNGramTokenFilterSide.Front } },
            Analyzers =
            {
                new CustomAnalyzer(exactAnalyzer, LexicalTokenizerName.Keyword)
                    { CharFilters = { "zwsp_strip" }, TokenFilters = { TokenFilterName.Lowercase, TokenFilterName.Trim } },
                new CustomAnalyzer(normAnalyzer, LexicalTokenizerName.Keyword)
                    { CharFilters = { normalizer }, TokenFilters = { TokenFilterName.Lowercase, TokenFilterName.Trim } },
                new CustomAnalyzer(prefixAnalyzer, LexicalTokenizerName.Keyword)
                    { CharFilters = { normalizer }, TokenFilters = { TokenFilterName.Lowercase, TokenFilterName.Trim, edge } },
            },
            ScoringProfiles =
            {
                new ScoringProfile(ScoringProfile)
                {
                    TextWeights = new TextWeights(new Dictionary<string, double>
                        { [ExactField] = 10, [NormField] = 5, [PrefixField] = 2, ["content"] = 1 }),
                },
            },
            DefaultScoringProfile = ScoringProfile,
        };
        // The exact analyzer keeps separators but must still ignore the zero-width space.
        index.CharFilters.Add(new MappingCharFilter("zwsp_strip", new[] { "\\u200B=>" }));
        return index;
    }
}

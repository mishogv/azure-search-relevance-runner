using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;

namespace SearchFix.Runner;

/// <summary>Thin wrapper over Azure AI Search: index lifecycle, upload, one search call.</summary>
public sealed class SearchBackend
{
    private readonly SearchIndexClient _indexClient;
    private readonly string _indexName;

    public SearchBackend(string indexName)
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT")
            ?? throw new InvalidOperationException("Set AZURE_SEARCH_ENDPOINT");
        var key = Environment.GetEnvironmentVariable("AZURE_SEARCH_ADMIN_KEY")
            ?? throw new InvalidOperationException("Set AZURE_SEARCH_ADMIN_KEY");
        _indexClient = new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(key));
        _indexName = indexName;
    }

    public SearchClient Client => _indexClient.GetSearchClient(_indexName);

    /// <summary>Drops and recreates the index with the given definition, then uploads all docs.</summary>
    public async Task RebuildAsync(SearchIndex definition, IReadOnlyList<IndexDoc> docs)
    {
        try { await _indexClient.DeleteIndexAsync(_indexName, CancellationToken.None); }
        catch (RequestFailedException e) when (e.Status == 404) { }
        await _indexClient.CreateIndexAsync(definition);

        // Every searchable string field receives the same source string (content, content_exact, …).
        var textFields = definition.Fields.Where(f => f.IsSearchable == true).Select(f => f.Name).ToList();
        var client = Client;
        foreach (var chunk in docs.Chunk(500))
        {
            var batch = IndexDocumentsBatch.Upload(chunk.Select(d =>
            {
                var doc = new SearchDocument { ["id"] = d.Id };
                foreach (var f in textFields) doc[f] = d.Content;
                return doc;
            }));
            await client.IndexDocumentsAsync(batch);
        }

        // Indexing is eventually consistent: wait until every document is countable.
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(1000);
            if (await client.GetDocumentCountAsync() >= docs.Count) return;
        }
        throw new InvalidOperationException("Index did not reach the expected document count");
    }

    /// <summary>Returns the `content` of every hit (top 100), or null when the service rejects the query.</summary>
    public async Task<(List<string>? Hits, string? Error)> SearchAsync(BuiltQuery q)
    {
        var options = new SearchOptions
        {
            IncludeTotalCount = true,
            Size = 100,
            QueryType = SearchQueryType.Full,
            SearchMode = q.Mode,
        };
        foreach (var f in q.SearchFields) options.SearchFields.Add(f);
        options.Select.Add("content");
        if (q.ScoringProfile is not null) options.ScoringProfile = q.ScoringProfile;

        try
        {
            var response = await Client.SearchAsync<SearchDocument>(q.Text, options);
            var hits = new List<string>();
            await foreach (var r in response.Value.GetResultsAsync())
                hits.Add(r.Document["content"]?.ToString() ?? "");
            return (hits, null);
        }
        catch (RequestFailedException e)
        {
            return (null, $"HTTP {e.Status}: {FirstLine(e.Message)}");
        }
    }

    /// <summary>Shows how an analyzer tokenizes a string (used for evidence in the report).</summary>
    public async Task<List<string>> AnalyzeAsync(string text, string analyzerName)
    {
        var result = await _indexClient.AnalyzeTextAsync(_indexName,
            new AnalyzeTextOptions(text, new LexicalAnalyzerName(analyzerName)));
        return result.Value.Select(t => t.Token).ToList();
    }

    private static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        return i < 0 ? s : s[..i];
    }
}

/// <summary>What a query builder hands to the service.</summary>
public sealed record BuiltQuery(string Text, SearchMode Mode, string[] SearchFields, string? ScoringProfile = null);

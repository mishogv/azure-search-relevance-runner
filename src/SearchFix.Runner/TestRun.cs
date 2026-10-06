using System.Text.Json;

namespace SearchFix.Runner;

/// <summary>Outcome of one case: did the document from "Searchable content" appear in the top 100?</summary>
public sealed record CaseResult(
    string Tab, string CaseId, string Category, string ToFixIn,
    string Query, string BuiltQuery, string Expected, string Actual, int Hits, string? Error)
{
    public bool Pass => Expected == Actual;
}

public static class TestRun
{
    /// <summary>
    /// Runs every case through the builder. `expectedOf` picks the column to compare with:
    /// "In current result" for the baseline, "Should be in result" for a fix.
    /// </summary>
    public static async Task<List<CaseResult>> RunAsync(
        SearchBackend backend, IQueryBuilder builder, IReadOnlyList<Case> cases, Func<Case, string> expectedOf)
    {
        var results = new List<CaseResult>(cases.Count);
        var done = 0;
        foreach (var c in cases)
        {
            var q = builder.Build(c.Query);
            var (hits, error) = await backend.SearchAsync(q);
            // The assert is exact string equality on the full, unmodified content (U+200B included).
            var actual = error is not null ? "ERROR" : (hits!.Contains(c.Content, StringComparer.Ordinal) ? "TRUE" : "FALSE");
            results.Add(new CaseResult(c.Tab, c.CaseId, c.Category, c.ToFixIn, c.Query, q.Text,
                expectedOf(c), actual, hits?.Count ?? 0, error));
            if (++done % 100 == 0) Console.Error.WriteLine($"  {done}/{cases.Count}");
        }
        return results;
    }

    public static void PrintSummary(IReadOnlyList<CaseResult> results)
    {
        Console.WriteLine($"\nTotal: {results.Count(r => r.Pass)}/{results.Count} pass");
        foreach (var tab in results.GroupBy(r => r.Tab))
        {
            var actuals = string.Join(", ", tab.GroupBy(r => r.Actual).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
            Console.WriteLine($"\n{tab.Key}: {tab.Count(r => r.Pass)}/{tab.Count()} pass  (actual: {actuals})");
            foreach (var step in tab.GroupBy(r => r.ToFixIn).OrderBy(g => g.Key))
            foreach (var cat in step.GroupBy(r => r.Category).OrderBy(g => g.Key))
            {
                var fails = cat.Where(r => !r.Pass).Select(r => r.CaseId).Take(6).ToList();
                var tail = fails.Count > 0 ? $"  fail: {string.Join(" ", fails)}{(cat.Count(r => !r.Pass) > 6 ? " …" : "")}" : "";
                Console.WriteLine($"  {step.Key,-7} {cat.Key,-28} {cat.Count(r => r.Pass),3}/{cat.Count(),-3}{tail}");
            }
        }
    }

    public static void Save(IReadOnlyList<CaseResult> results, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static List<CaseResult> Load(string path) =>
        JsonSerializer.Deserialize<List<CaseResult>>(File.ReadAllText(path))!;

    /// <summary>Diff two saved runs: which cases changed outcome. This is the regression tool across runs.</summary>
    public static void Compare(IReadOnlyList<CaseResult> before, IReadOnlyList<CaseResult> after)
    {
        var b = before.ToDictionary(r => r.CaseId);
        var fixedCount = 0; var broken = 0; var changed = 0;
        foreach (var a in after)
        {
            if (!b.TryGetValue(a.CaseId, out var prev) || prev.Actual == a.Actual) continue;
            changed++;
            if (!prev.Pass && a.Pass) fixedCount++;
            else if (prev.Pass && !a.Pass) { broken++; Console.WriteLine($"BROKEN {a.CaseId} [{a.Category}] {prev.Actual} -> {a.Actual}  q={a.Query}"); }
            else Console.WriteLine($"changed {a.CaseId} [{a.Category}] {prev.Actual} -> {a.Actual}  q={a.Query}");
        }
        Console.WriteLine($"\nchanged {changed}: fixed {fixedCount}, broken {broken}");
    }
}

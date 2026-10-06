using SearchFix.Runner;

namespace SearchFix.Tests;

/// <summary>Offline checks: the loader reads the sheet by header names and the baseline column matches the brief.</summary>
public class WorkbookTests
{
    private static string Path_ => FindRoot("data/azure-search-test-cases.xlsx");

    [Fact]
    public void IndexData_has_724_documents_with_zero_width_spaces_preserved()
    {
        var docs = Workbook.LoadIndexData(Path_);
        Assert.Equal(724, docs.Count);
        Assert.Contains(docs, d => d.Content.Contains('​'));
        Assert.Equal(724, docs.Select(d => d.Content).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Cases_match_the_brief_baseline_distribution()
    {
        var cases = Workbook.LoadCases(Path_);
        Assert.Equal(710, cases.Count);

        int Count(string tab, string current) => cases.Count(c => c.Tab == tab && c.InCurrentResult == current);
        Assert.Equal(80, Count("Must Find", "FALSE"));
        Assert.Equal(43, Count("Must Find", "ERROR"));
        Assert.Equal(98, Count("Must Suppress", "TRUE"));
        Assert.Equal(131, Count("Regression Guard - Must Find", "TRUE"));
        Assert.Equal(358, Count("RG - Must Suppress", "FALSE"));
    }

    [Fact]
    public void Every_case_content_exists_in_index_data()
    {
        var docs = Workbook.LoadIndexData(Path_).Select(d => d.Content).ToHashSet(StringComparer.Ordinal);
        var missing = Workbook.LoadCases(Path_).Where(c => !docs.Contains(c.Content)).Select(c => c.CaseId).ToList();
        Assert.Empty(missing);
    }

    private static string FindRoot(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, relative);
    }
}

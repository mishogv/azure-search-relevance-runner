using ClosedXML.Excel;

namespace SearchFix.Runner;

/// <summary>One row of a case tab. Strings are kept exactly as stored (no trimming: some contain U+200B).</summary>
public sealed record Case(
    string Tab,
    string CaseId,
    string Content,
    string Query,
    string ShouldBeInResult,   // TRUE / FALSE
    string InCurrentResult,    // TRUE / FALSE / ERROR
    string Category,
    string ToFixIn);           // Step 2 / Step 3 / "" (regression tabs)

public sealed record IndexDoc(string Id, string Content);

public static class Workbook
{
    public static readonly string[] CaseTabs =
    {
        "Must Find", "Must Suppress", "Regression Guard - Must Find", "RG - Must Suppress",
    };

    public static List<IndexDoc> LoadIndexData(string path)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Index Data");
        var cols = HeaderMap(ws);
        var docs = new List<IndexDoc>();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var id = Cell(row, cols["id"]);
            if (id.Length == 0) continue;
            docs.Add(new IndexDoc(id, Cell(row, cols["content"])));
        }
        return docs;
    }

    public static List<Case> LoadCases(string path)
    {
        using var wb = new XLWorkbook(path);
        var cases = new List<Case>();
        foreach (var tab in CaseTabs)
        {
            var ws = wb.Worksheet(tab);
            var cols = HeaderMap(ws);
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var caseId = Cell(row, cols["Case ID"]);
                if (caseId.Length == 0) continue;
                cases.Add(new Case(
                    tab,
                    caseId,
                    Cell(row, cols["Searchable content"]),
                    Cell(row, cols["Search query"]),
                    Cell(row, cols["Should be in result"]).ToUpperInvariant(),
                    Cell(row, cols["In current result"]).ToUpperInvariant(),
                    Cell(row, cols["Category"]),
                    cols.TryGetValue("To fix in", out var fix) ? Cell(row, fix) : ""));
            }
        }
        return cases;
    }

    // Column lookup by header text, as the brief asks ("use column headers, not fixed column letters").
    private static Dictionary<string, int> HeaderMap(IXLWorksheet ws)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in ws.Row(1).CellsUsed())
            map[cell.GetString().Trim()] = cell.Address.ColumnNumber;
        return map;
    }

    // Query/content cells are read raw; only the structural columns are trimmed by the callers above.
    private static string Cell(IXLRow row, int col) => row.Cell(col).GetString();
}

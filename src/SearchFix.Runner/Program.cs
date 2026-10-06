using SearchFix.Runner;

// Usage:
//   runner baseline [--rebuild]      reproduce the current behaviour against the "In current result" column
//   runner compare <a.json> <b.json> diff two saved runs
//   runner analyze <analyzer> <text> show how an analyzer tokenizes text
// Env: AZURE_SEARCH_ENDPOINT, AZURE_SEARCH_ADMIN_KEY. Workbook: data/azure-search-test-cases.xlsx.

var args0 = args.Length > 0 ? args[0] : "help";
var root = FindRoot();
var workbook = Path.Combine(root, "data", "azure-search-test-cases.xlsx");
var resultsDir = Path.Combine(root, "results");

switch (args0)
{
    case "baseline":
    {
        var backend = new SearchBackend("searchfix-baseline");
        var docs = Workbook.LoadIndexData(workbook);
        var cases = Workbook.LoadCases(workbook);
        Console.WriteLine($"{docs.Count} documents, {cases.Count} cases");
        if (args.Contains("--rebuild"))
        {
            Console.WriteLine("rebuilding index…");
            await backend.RebuildAsync(Indexes.Baseline("searchfix-baseline"), docs);
        }
        var results = await TestRun.RunAsync(backend, new CurrentQueryBuilder(), cases, c => c.InCurrentResult);
        TestRun.PrintSummary(results);
        TestRun.Save(results, Path.Combine(resultsDir, "baseline.json"));
        break;
    }
    case "step2":
    {
        // Same baseline index: Step 2 changes only the query.
        var backend = new SearchBackend("searchfix-baseline");
        var cases = Workbook.LoadCases(workbook);
        var results = await TestRun.RunAsync(backend, new Step2QueryBuilder(), cases, c => c.ShouldBeInResult);
        TestRun.PrintSummary(results);
        TestRun.Save(results, Path.Combine(resultsDir, "step2.json"));
        break;
    }
    case "step3":
    {
        var backend = new SearchBackend("searchfix-step3");
        var docs = Workbook.LoadIndexData(workbook);
        var cases = Workbook.LoadCases(workbook);
        if (args.Contains("--rebuild"))
        {
            Console.WriteLine("rebuilding step3 index…");
            await backend.RebuildAsync(Indexes.Step3("searchfix-step3"), docs);
        }
        var results = await TestRun.RunAsync(backend, new Step3QueryBuilder(), cases, c => c.ShouldBeInResult);
        TestRun.PrintSummary(results);
        TestRun.Save(results, Path.Combine(resultsDir, "step3.json"));
        break;
    }
    case "compare":
        TestRun.Compare(TestRun.Load(args[1]), TestRun.Load(args[2]));
        break;
    case "analyze":
    {
        var at = Array.IndexOf(args, "--index");
        var backend = new SearchBackend(at > 0 && at + 1 < args.Length ? args[at + 1] : "searchfix-baseline");
        var tokens = await backend.AnalyzeAsync(args[2], args[1]);
        Console.WriteLine($"{args[1]}: [{string.Join("] [", tokens)}]");
        break;
    }
    default:
        Console.WriteLine("commands: baseline [--rebuild] | compare <a.json> <b.json> | analyze <analyzer> <text>");
        break;
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("data folder not found above " + AppContext.BaseDirectory);
}

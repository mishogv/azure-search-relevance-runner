# Azure AI Search relevance — test runner

Reproduces the search behaviour described in the developer brief against a real Azure AI Search
index and checks all 710 cases from `data/azure-search-test-cases.xlsx`. One command per step.

## Layout

- `src/SearchFix.Runner` — console app: workbook loader, index definitions, query builders, test loop, run diff.
- `tests/SearchFix.Tests` — xUnit: workbook contract (counts from the brief), query-encoding decision table.
- `data/` — put the client's `azure-search-test-cases.xlsx` here (not in the repo; it is the client's material). Read as provided: zero-width spaces included, never normalised.
- `results/` — one JSON per run (`baseline.json`, `step2.json`, `step3.json`) for `compare`; `tools/deviations.py` groups a run's mismatches. Kept out of the repo for the same reason.

## Query variants

| Name | Index | Query construction |
|---|---|---|
| `current` | standard `content` field | the production code, verbatim (regex/OR wrapper, `SearchMode.Any`) |
| `step2` | same index | `SearchMode.All`, OR/AND keyword detection kept, escaping rewritten (no regex) |
| `step3` | + `content_exact`, `content_norm`, `content_prefix` | fielded query: exact^10 OR normalized^5 OR prefix^2 OR free text |

## Run

```
set AZURE_SEARCH_ENDPOINT=https://<service>.search.windows.net
set AZURE_SEARCH_ADMIN_KEY=<admin key>

dotnet test
dotnet run --project src/SearchFix.Runner -- baseline --rebuild   # compare with "In current result"
dotnet run --project src/SearchFix.Runner -- step2               # compare with "Should be in result"
dotnet run --project src/SearchFix.Runner -- step3 --rebuild
dotnet run --project src/SearchFix.Runner -- compare results/baseline.json results/step2.json
dotnet run --project src/SearchFix.Runner -- analyze standard.lucene "000_081_835"
```

The assert is always: run the query with `top=100` and check whether a document whose `content`
equals the row's `Searchable content` (ordinal comparison, untrimmed) is in the result set.
Any request the service rejects is recorded as `ERROR`, which is how the sheet marks HTTP 500 cases.

## Measured on the sheet (free tier, 5 Oct 2026)

| Run | Rows matching the sheet |
|---|---|
| current code, verbatim (vs. "In current result") | 488 / 710 |
| step2 (vs. "Should be in result") | 564 / 710 |
| step3 | 708 / 710 (remaining: one infix case, one lone-quote case) |

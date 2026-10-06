"""Group the mismatches of a saved run by tab / category / expected→actual, with examples.

usage: python tools/deviations.py results/baseline.json [--examples 3]
"""
import json
import sys
from collections import defaultdict

path = sys.argv[1]
n_ex = int(sys.argv[sys.argv.index("--examples") + 1]) if "--examples" in sys.argv else 3
rows = json.load(open(path, encoding="utf-8"))

groups = defaultdict(list)
for r in rows:
    if r["Expected"] != r["Actual"]:
        groups[(r["Tab"], r["Category"], r["Expected"], r["Actual"])].append(r)

total_bad = sum(len(v) for v in groups.values())
print(f"{len(rows)} rows, {len(rows) - total_bad} match, {total_bad} deviate\n")
for (tab, cat, exp, act), items in sorted(groups.items(), key=lambda kv: (-len(kv[1]), kv[0])):
    capped = sum(1 for r in items if r["Hits"] >= 100)
    print(f"{len(items):3d}  {tab} | {cat} | sheet {exp} → azure {act}" + (f"  ({capped} with 100 hits = top cut)" if capped else ""))
    for r in items[:n_ex]:
        err = f"  [{r['Error']}]" if r.get("Error") else ""
        print(f"       {r['CaseId']}: q={r['Query']!r} hits={r['Hits']}{err}")

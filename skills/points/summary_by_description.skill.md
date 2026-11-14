---
name: summary_by_description
category: points
description: Count COGO points per raw description and elevation range.
tags: [points,cogo,report,description]
mode: query
---

# Summary By Description

Count COGO points per raw description and elevation range.

Run with `code_query`. Values come in through `inputs`.

```csharp
var d = new Dictionary<string, (int n, double min, double max)>();
foreach (ObjectId id in CivilDoc.CogoPoints)
{
    var p = (CogoPoint)Transaction.GetObject(id, OpenMode.ForRead);
    var key = string.IsNullOrEmpty(p.RawDescription) ? "(blank)" : p.RawDescription;
    d.TryGetValue(key, out var v);
    d[key] = v.n == 0 ? (1, p.Elevation, p.Elevation) : (v.n + 1, Math.Min(v.min, p.Elevation), Math.Max(v.max, p.Elevation));
}
return d.OrderByDescending(x => x.Value.n).Select(x => new { desc = x.Key, count = x.Value.n, zmin = x.Value.min, zmax = x.Value.max }).ToList();
```

---
name: count_by_layer
category: drawing
description: Count objects per layer and type in model space (quick drawing audit).
tags: [audit,layer,count,report]
mode: query
---

# Count By Layer

Count objects per layer and type in model space (quick drawing audit).

Run with `code_query`. Values come in through `inputs`.

```csharp
var ms = Ctx.ModelSpace();
return ms.Cast<ObjectId>()
    .Select(id => (Entity)Transaction.GetObject(id, OpenMode.ForRead))
    .GroupBy(e => e.Layer)
    .Select(g => new { layer = g.Key, total = g.Count(), types = g.GroupBy(e => e.GetType().Name).ToDictionary(x => x.Key, x => x.Count()) })
    .OrderByDescending(x => x.total).ToList();
```

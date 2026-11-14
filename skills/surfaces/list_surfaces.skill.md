---
name: list_surfaces
category: surfaces
description: List every surface with point count and min/max/mean elevation.
tags: [surface,list,statistics]
mode: query
---

# List Surfaces

List every surface with point count and min/max/mean elevation.

Run with `code_query`. Values come in through `inputs`.

```csharp
var list = new List<object>();
foreach (ObjectId id in CivilDoc.GetSurfaceIds())
{
    var s = (Surface)Transaction.GetObject(id, OpenMode.ForRead);
    var p = s.GetGeneralProperties();
    list.Add(new { s.Name, Handle = s.Handle.ToString(), Type = s.GetType().Name,
                   p.NumberOfPoints, p.MinimumElevation, p.MaximumElevation, p.MeanElevation });
}
return list;
```

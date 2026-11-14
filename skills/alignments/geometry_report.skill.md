---
name: geometry_report
category: alignments
description: Report every tangent/curve/spiral of an alignment (stations, lengths, radii).
tags: [alignment,report,geometry,curve]
mode: query
---

# Geometry Report

Report every tangent/curve/spiral of an alignment (stations, lengths, radii).

Run with `code_query`. Values come in through `inputs`.

Example inputs:

```json
{"alignment": "CL-1"}
```

```csharp
var al = (Alignment)Transaction.GetObject(Ctx.Find((string)Inputs["alignment"]), OpenMode.ForRead);
var rows = new List<object>();
foreach (AlignmentEntity e in al.Entities)
{
    switch (e)
    {
        case AlignmentLine l: rows.Add(new { type = "Line", l.StartStation, l.EndStation, l.Length, dir_deg = l.Direction * 180 / Math.PI }); break;
        case AlignmentArc a:  rows.Add(new { type = "Curve", a.StartStation, a.EndStation, a.Length, a.Radius }); break;
        default: rows.Add(new { type = e.EntityType.ToString() }); break;
    }
}
return rows;
```

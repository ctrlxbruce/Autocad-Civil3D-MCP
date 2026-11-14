---
name: offset_both_sides
category: geometry
description: Offset a curve to both sides (e.g. road edges from a centreline polyline).
tags: [offset,road,edge,geometry]
mode: execute
---

# Offset Both Sides

Offset a curve to both sides (e.g. road edges from a centreline polyline).

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"curve": "2B1", "distance": 3.5, "layer": "C-ROAD-EDGE"}
```

```csharp
var cv = (Curve)Transaction.GetObject(Ctx.Find((string)Inputs["curve"]), OpenMode.ForRead);
double d = Convert.ToDouble(Inputs["distance"]);
var made = new List<string>();
foreach (var dist in new[] { d, -d })
    foreach (Entity e in cv.GetOffsetCurves(dist))
    {
        if (Inputs.ContainsKey("layer")) { Ctx.EnsureLayer((string)Inputs["layer"]); e.Layer = (string)Inputs["layer"]; }
        made.Add(Ctx.Add(e).Handle.ToString());
    }
return made;
```

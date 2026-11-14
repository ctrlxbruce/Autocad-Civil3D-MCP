---
name: add_boundary
category: surfaces
description: Add an outer boundary to a surface from a closed polyline.
tags: [surface,boundary,clip]
mode: execute
---

# Add Boundary

Add an outer boundary to a surface from a closed polyline.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"surface": "EG", "polyline": "2F4"}
```

```csharp
var surf = (TinSurface)Transaction.GetObject(Ctx.Find((string)Inputs["surface"]), OpenMode.ForWrite);
var ids = new ObjectIdCollection { Ctx.Find((string)Inputs["polyline"]) };
surf.BoundariesDefinition.AddBoundaries(ids, 0.1, Autodesk.Civil.SurfaceBoundaryType.Outer, true);
surf.Rebuild();
return surf.GetGeneralProperties().NumberOfPoints;
```

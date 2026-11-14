---
name: eg_from_csv
category: workflows
description: Full workflow: import CSV points → point group → EG surface → contour style.
tags: [workflow,survey,surface,points,contour]
mode: execute
---

# Eg From Csv

Full workflow: import CSV points → point group → EG surface → contour style.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"path": "D:\\survey\\pts.csv", "surface": "EG"}
```

```csharp
// 1) import
var fmt = PointFileFormatCollection.GetPointFileFormats(Database)[(string)(Inputs.ContainsKey("format") ? Inputs["format"] : "PNEZD (comma delimited)")];
uint n = CivilDoc.CogoPoints.ImportPoints((string)Inputs["path"], fmt);
// 2) group with all points
var groups = CivilDoc.PointGroups;
ObjectId gid = groups.Contains("SURVEY") ? groups["SURVEY"] : groups.Add("SURVEY");
var pg = (PointGroup)Transaction.GetObject(gid, OpenMode.ForWrite);
pg.SetQuery(new StandardPointGroupQuery { IncludeNumbers = "1-999999" });
pg.Update();
// 3) surface
ObjectId sid = TinSurface.Create(Database, (string)(Inputs.ContainsKey("surface") ? Inputs["surface"] : "EG"));
var surf = (TinSurface)Transaction.GetObject(sid, OpenMode.ForWrite);
surf.PointGroupsDefinition.AddPointGroup(gid);
surf.Rebuild();
Print($"Imported {n} points; surface has {surf.GetGeneralProperties().NumberOfPoints} points.");
return new { imported = n, surface = surf.Name, max_elevation = surf.GetGeneralProperties().MaximumElevation };
```

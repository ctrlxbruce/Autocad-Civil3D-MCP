---
name: create_from_point_group
category: surfaces
description: Create a TIN surface from a point group and rebuild it.
tags: [surface,tin,point group,create]
mode: execute
---

# Create From Point Group

Create a TIN surface from a point group and rebuild it.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"name": "EG", "group": "SURVEY"}
```

```csharp
string name  = (string)Inputs["name"];    // e.g. "EG"
string group = (string)Inputs["group"];   // e.g. "SURVEY"
ObjectId sid = TinSurface.Create(Database, name);
var surf = (TinSurface)Transaction.GetObject(sid, OpenMode.ForWrite);
surf.PointGroupsDefinition.AddPointGroup(CivilDoc.PointGroups[group]);
surf.Rebuild();
var p = surf.GetGeneralProperties();
return new { Handle = surf.Handle.ToString(), p.NumberOfPoints, p.MinimumElevation, p.MaximumElevation };
```

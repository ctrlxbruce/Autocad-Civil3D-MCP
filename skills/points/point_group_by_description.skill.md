---
name: point_group_by_description
category: points
description: Create a point group from raw description patterns (e.g. TREE*, MH*).
tags: [points,point group,description,query]
mode: execute
---

# Point Group By Description

Create a point group from raw description patterns (e.g. TREE*, MH*).

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"name": "TREES", "include": "TREE*,TR*"}
```

```csharp
string name = (string)Inputs["name"];
var groups = CivilDoc.PointGroups;
ObjectId id = groups.Contains(name) ? groups[name] : groups.Add(name);
var pg = (PointGroup)Transaction.GetObject(id, OpenMode.ForWrite);
var q = new StandardPointGroupQuery { IncludeRawDescriptions = (string)Inputs["include"] };
pg.SetQuery(q);
pg.Update();
return new { name, pg.PointsCount };
```

---
name: layer_standard
category: drawing
description: Create a set of layers with colour, linetype and lineweight in one go.
tags: [layer,standard,create,color]
mode: execute
---

# Layer Standard

Create a set of layers with colour, linetype and lineweight in one go.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"layers": [["C-ROAD","2","Continuous",0.35],["C-TOPO-MAJR","1","Continuous",0.35],["C-TOPO-MINR","8","Continuous",0.18],["C-PROP","6","PHANTOM",0.25]]}
```

```csharp
// Inputs["layers"] = [["C-ROAD","2","Continuous",0.35], ["C-TOPO-MAJR","1","Continuous",0.35], ...]
var lt = (LayerTable)Transaction.GetObject(Database.LayerTableId, OpenMode.ForWrite);
var ltt = (LinetypeTable)Transaction.GetObject(Database.LinetypeTableId, OpenMode.ForRead);
var made = new List<string>();
foreach (var row in (System.Text.Json.Nodes.JsonArray)Inputs["layers"])
{
    var r = row.AsArray();
    string name = r[0].ToString();
    LayerTableRecord rec = lt.Has(name) ? (LayerTableRecord)Transaction.GetObject(lt[name], OpenMode.ForWrite) : new LayerTableRecord { Name = name };
    rec.Color = Conv.ToColor(r[1]);
    string ltype = r.Count > 2 ? r[2].ToString() : "Continuous";
    if (!ltt.Has(ltype)) Database.LoadLineTypeFile(ltype, "acad.lin");
    rec.LinetypeObjectId = ((LinetypeTable)Transaction.GetObject(Database.LinetypeTableId, OpenMode.ForRead))[ltype];
    if (r.Count > 3) rec.LineWeight = Conv.ToLineWeight(r[3]);
    if (rec.ObjectId.IsNull) { lt.Add(rec); Transaction.AddNewlyCreatedDBObject(rec, true); }
    made.Add(name);
}
return made;
```

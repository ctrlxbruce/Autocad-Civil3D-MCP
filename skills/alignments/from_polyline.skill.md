---
name: from_polyline
category: alignments
description: Create an alignment from a polyline (curves between tangents optional).
tags: [alignment,create,polyline]
mode: execute
---

# From Polyline

Create an alignment from a polyline (curves between tangents optional).

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"polyline": "2B1", "name": "CL-1", "curves": true}
```

```csharp
var opts = new PolylineOptions
{
    PlineId = Ctx.Find((string)Inputs["polyline"]),
    AddCurvesBetweenTangents = Inputs.ContainsKey("curves") && (bool)Inputs["curves"],
    EraseExistingEntities = false
};
ObjectId id = Alignment.Create(CivilDoc, opts, (string)Inputs["name"], ObjectId.Null, Database.Clayer,
    CivilDoc.Styles.AlignmentStyles[0], CivilDoc.Styles.LabelSetStyles.AlignmentLabelSetStyles[0]);
var al = (Alignment)Transaction.GetObject(id, OpenMode.ForRead);
return new { al.Name, Handle = al.Handle.ToString(), al.StartingStation, al.EndingStation, al.Length };
```

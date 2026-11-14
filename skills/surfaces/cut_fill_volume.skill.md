---
name: cut_fill_volume
category: surfaces
description: Cut/fill/net volume between an existing and a design surface.
tags: [surface,volume,cut,fill,earthwork]
mode: execute
---

# Cut Fill Volume

Cut/fill/net volume between an existing and a design surface.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"base": "EG", "comparison": "FG"}
```

```csharp
ObjectId baseId = Ctx.Find((string)Inputs["base"]);        // existing ground
ObjectId compId = Ctx.Find((string)Inputs["comparison"]);  // design
string name = Inputs.ContainsKey("name") ? (string)Inputs["name"] : "VOL";
ObjectId v = TinVolumeSurface.Create(name, baseId, compId);
var vs = (TinVolumeSurface)Transaction.GetObject(v, OpenMode.ForRead);
var p = vs.GetVolumeProperties();
return new { cut = p.UnadjustedCutVolume, fill = p.UnadjustedFillVolume, net = p.UnadjustedNetVolume };
```

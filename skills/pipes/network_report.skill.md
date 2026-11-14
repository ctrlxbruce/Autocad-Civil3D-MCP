---
name: network_report
category: pipes
description: Report pipes (length, slope, diameter) and structures (rim, sump) of a network.
tags: [pipe,network,report,structure]
mode: query
---

# Network Report

Report pipes (length, slope, diameter) and structures (rim, sump) of a network.

Run with `code_query`. Values come in through `inputs`.

Example inputs:

```json
{"network": "Storm"}
```

```csharp
var net = (Network)Transaction.GetObject(Ctx.Find((string)Inputs["network"]), OpenMode.ForRead);
var pipes = net.GetPipeIds().Cast<ObjectId>().Select(id => (Pipe)Transaction.GetObject(id, OpenMode.ForRead))
    .Select(p => new { p.Name, p.Length2D, slope_pct = p.Slope * 100, dia = p.InnerDiameterOrWidth }).ToList();
var structs = net.GetStructureIds().Cast<ObjectId>().Select(id => (Structure)Transaction.GetObject(id, OpenMode.ForRead))
    .Select(s => new { s.Name, s.RimElevation, s.SumpElevation }).ToList();
return new { pipes, structures = structs };
```

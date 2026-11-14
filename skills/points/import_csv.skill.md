---
name: import_csv
category: points
description: Import COGO points from a CSV/TXT file with a named file format.
tags: [points,cogo,import,csv,pnezd]
mode: execute
---

# Import Csv

Import COGO points from a CSV/TXT file with a named file format.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"path": "D:\\survey\\points.csv", "format": "PNEZD (comma delimited)"}
```

```csharp
string path = (string)Inputs["path"];
string fmtName = Inputs.ContainsKey("format") ? (string)Inputs["format"] : "PNEZD (comma delimited)";
var fmts = PointFileFormatCollection.GetPointFileFormats(Database);
var fmt = fmts[fmtName];
uint n = CivilDoc.CogoPoints.ImportPoints(path, fmt);
return new { imported = n, total = CivilDoc.CogoPoints.Count };
```

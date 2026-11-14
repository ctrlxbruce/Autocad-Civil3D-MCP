---
name: label_polyline_lengths
category: drawing
description: Put a text label with the length on every segment of a polyline.
tags: [label,length,polyline,text,annotation]
mode: execute
---

# Label Polyline Lengths

Put a text label with the length on every segment of a polyline.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"polyline": "2C7", "height": 2.5}
```

```csharp
var pl = (Polyline)Transaction.GetObject(Ctx.Find((string)Inputs["polyline"]), OpenMode.ForRead);
double h = Inputs.ContainsKey("height") ? Convert.ToDouble(Inputs["height"]) : 2.5;
int n = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
var made = new List<string>();
for (int i = 0; i < n; i++)
{
    if (pl.GetSegmentType(i) != SegmentType.Line) continue;
    var seg = pl.GetLineSegmentAt(i);
    double ang = Math.Atan2(seg.Direction.Y, seg.Direction.X);
    if (ang > Math.PI / 2 || ang < -Math.PI / 2) ang += Math.PI;       // keep text readable
    var t = new MText { Contents = seg.Length.ToString("0.00"), TextHeight = h, Rotation = ang,
                        Attachment = AttachmentPoint.BottomCenter,
                        Location = seg.MidPoint + seg.Direction.GetPerpendicularVector() * h * 0.3 };
    made.Add(Ctx.Add(t).Handle.ToString());
}
return made;
```

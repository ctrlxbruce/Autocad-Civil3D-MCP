---
name: contour_style
category: surfaces
description: Create/update a surface style with major/minor contours, smoothing on, triangles and points off.
tags: [surface,style,contour,smoothing,interval]
mode: execute
---

# Contour Style

Create/update a surface style with major/minor contours, smoothing on, triangles and points off.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"style": "Contours 1m and 5m (Smooth)", "major": 5, "minor": 1, "surface": "EG"}
```

```csharp
string name  = (string)Inputs["style"];
double major = Convert.ToDouble(Inputs["major"]);   // e.g. 5
double minor = Convert.ToDouble(Inputs["minor"]);   // e.g. 1
var styles = CivilDoc.Styles.SurfaceStyles;
ObjectId id = styles.Contains(name) ? styles[name] : styles.Add(name);
var st = (SurfaceStyle)Transaction.GetObject(id, OpenMode.ForWrite);
st.ContourStyle.MajorContourInterval = major;
st.ContourStyle.MinorContourInterval = minor;
st.ContourStyle.SmoothContours = true;
st.ContourStyle.SmoothingType = ContourSmoothingType.SplineCurve;   // or AddVertices
st.ContourStyle.SmoothingFactor = 5;                                // 1-10
var show = new Dictionary<SurfaceDisplayStyleType, bool>
{
    [SurfaceDisplayStyleType.MajorContour] = true,
    [SurfaceDisplayStyleType.MinorContour] = true,
    [SurfaceDisplayStyleType.Triangles] = false,
    [SurfaceDisplayStyleType.Points] = false,
    [SurfaceDisplayStyleType.Boundary] = true,
};
foreach (var kv in show) st.GetDisplayStylePlan(kv.Key).Visible = kv.Value;
// optional: apply to a surface
if (Inputs.ContainsKey("surface"))
{
    var s = (Surface)Transaction.GetObject(Ctx.Find((string)Inputs["surface"]), OpenMode.ForWrite);
    s.StyleId = id;
}
return new { style = name, major, minor };
```

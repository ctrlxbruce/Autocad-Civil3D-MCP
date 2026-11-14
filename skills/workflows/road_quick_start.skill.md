---
name: road_quick_start
category: workflows
description: Workflow: polyline → alignment → EG profile → profile view, ready for FG design.
tags: [workflow,road,alignment,profile]
mode: execute
---

# Road Quick Start

Workflow: polyline → alignment → EG profile → profile view, ready for FG design.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"polyline": "2B1", "name": "Road A", "surface": "EG", "view_at": [0, -400]}
```

```csharp
var st = CivilDoc.Styles;
var opts = new PolylineOptions { PlineId = Ctx.Find((string)Inputs["polyline"]), AddCurvesBetweenTangents = true };
ObjectId al = Alignment.Create(CivilDoc, opts, (string)Inputs["name"], ObjectId.Null, Database.Clayer,
    st.AlignmentStyles[0], st.LabelSetStyles.AlignmentLabelSetStyles[0]);
ObjectId eg = Profile.CreateFromSurface("EG", al, Ctx.Find((string)Inputs["surface"]), Database.Clayer,
    st.ProfileStyles[0], st.LabelSetStyles.ProfileLabelSetStyles[0]);
ObjectId pv = ProfileView.Create(al, Conv.ToPoint3d(Inputs["view_at"]), (string)Inputs["name"] + " PV",
    st.ProfileViewBandSetStyles[0], st.ProfileViewStyles[0]);
return new { alignment = al.Handle.ToString(), eg = eg.Handle.ToString(), view = pv.Handle.ToString() };
```

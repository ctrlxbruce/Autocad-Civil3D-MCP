---
name: eg_profile_and_view
category: alignments
description: Create an existing-ground profile from a surface and a profile view.
tags: [profile,profile view,surface,eg]
mode: execute
---

# Eg Profile And View

Create an existing-ground profile from a surface and a profile view.

Run with `code_execute`. Values come in through `inputs`.

Example inputs:

```json
{"alignment": "CL-1", "surface": "EG", "position": [0, -300]}
```

```csharp
ObjectId alId = Ctx.Find((string)Inputs["alignment"]);
ObjectId sId  = Ctx.Find((string)Inputs["surface"]);
var st = CivilDoc.Styles;
ObjectId eg = Profile.CreateFromSurface("EG - " + Inputs["surface"], alId, sId, Database.Clayer,
    st.ProfileStyles[0], st.LabelSetStyles.ProfileLabelSetStyles[0]);
var at = Conv.ToPoint3d(Inputs["position"]);
ObjectId pv = ProfileView.Create(alId, at, "PV - " + Inputs["alignment"], st.ProfileViewBandSetStyles[0], st.ProfileViewStyles[0]);
return new { profile = eg.Handle.ToString(), view = pv.Handle.ToString() };
```

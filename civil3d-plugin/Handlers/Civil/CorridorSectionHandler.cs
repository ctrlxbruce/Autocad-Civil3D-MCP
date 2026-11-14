using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    /// <summary>Assemblies, corridors (baselines, regions, frequency, surfaces), sample lines,
    /// section views (cross sections), quantity take-off helpers.</summary>
    public static class CorridorSectionHandler
    {
        [Tool("assembly.create")]
        public static object AssemblyCreate(Args a, Ctx c)
        {
            var coll = Reflect.Get(c.Civ, "AssemblyCollection");
            var id = (ObjectId)Reflect.Call(coll, "Add", a.NeedStr("name"), a.Str("type", "UndividedCrownedRoad"), a.Pt("position"));
            return new
            {
                handle = Ctx.H(id),
                note = "Add subassemblies (lanes, curbs, daylight) from the Tool Palettes, or with props.call on the assembly."
            };
        }

        [Tool("corridor.create")]
        public static object CorridorCreate(Args a, Ctx c)
        {
            var coll = Reflect.Get(c.Civ, "CorridorCollection");
            var name = a.Str("name", "Corridor");
            var cid = (ObjectId)Reflect.Call(coll, "Add", name);
            var corr = c.Open(cid, true);
            var al = c.Find(a.NeedStr("alignment"));
            var pr = c.Find(a.NeedStr("profile"));
            var bl = Reflect.Call(Reflect.Get(corr, "Baselines"), "Add", a.Str("baseline_name", "Baseline (1)"), al, pr);
            var alObj = c.Open(al);
            double st = a.Dbl("start_station", Convert.ToDouble(Reflect.Get(alObj, "StartingStation")));
            double en = a.Dbl("end_station", Convert.ToDouble(Reflect.Get(alObj, "EndingStation")));
            var reg = Reflect.Call(Reflect.Get(bl, "BaselineRegions"), "Add", a.Str("region_name", "Region (1)"), c.Find(a.NeedStr("assembly")), st, en);
            var warnings = new List<string>();
            if (a.Has("frequency"))
            {
                try
                {
                    var set = Reflect.Get(reg, "AppliedAssemblySetting");
                    foreach (var p in new[] { "FrequencyAlongTangents", "FrequencyAlongCurves", "FrequencyAlongSpirals", "FrequencyAlongProfileCurves" })
                        try { Reflect.Set(set, p, a.Dbl("frequency")); } catch { }
                }
                catch (Exception ex) { warnings.Add("frequency: " + ex.Message); }
            }
            if (a.Has("style")) try { Reflect.Set(corr, "StyleId", c.StyleId("CorridorStyles", a.Str("style"))); } catch (Exception ex) { warnings.Add(ex.Message); }
            Reflect.Call(corr, "Rebuild");
            return new { handle = Ctx.H(cid), name, warnings };
        }

        [Tool("corridor.rebuild")]
        public static object Rebuild(Args a, Ctx c)
        {
            var corr = c.Open(a.NeedStr("corridor"), true);
            Reflect.Call(corr, "Rebuild");
            return new { ok = true };
        }

        /// <summary>Corridor surface from link codes (e.g. "Top", "Datum") and optional feature line codes.</summary>
        [Tool("corridor.add_surface")]
        public static object AddSurface(Args a, Ctx c)
        {
            var corr = c.Open(a.NeedStr("corridor"), true);
            var cs = Reflect.Call(Reflect.Get(corr, "CorridorSurfaces"), "Add", a.Str("name", "Corridor Top"));
            foreach (var code in a.Strs("link_codes")) Reflect.Call(cs, "AddLinkCode", code, true);
            foreach (var code in a.Strs("feature_line_codes")) try { Reflect.Call(cs, "AddFeatureLineCode", code); } catch { }
            if (a.Has("style")) try { Reflect.Set(cs, "SurfaceStyleId", c.StyleId("SurfaceStyles", a.Str("style"))); } catch { }
            Reflect.Call(corr, "Rebuild");
            return Props.Pick(cs, "Name", "SurfaceId");
        }

        // ---------------------------------------------------------------- sections
        /// <summary>
        /// Sample lines along an alignment. Give stations [..] OR interval (+ start/end),
        /// left/right swath widths, and surfaces/corridors to sample.
        /// </summary>
        [Tool("sections.sample_lines")]
        public static object SampleLines(Args a, Ctx c)
        {
            var alId = c.Find(a.NeedStr("alignment"));
            var al = c.Open(alId);
            var gid = (ObjectId)Reflect.CallStatic(Reflect.T("SampleLineGroup"), "Create", a.Str("group_name", "SL Collection"), alId);
            var stations = a.Dbls("stations");
            if (stations.Count == 0)
            {
                double s0 = a.Dbl("start_station", Convert.ToDouble(Reflect.Get(al, "StartingStation")));
                double s1 = a.Dbl("end_station", Convert.ToDouble(Reflect.Get(al, "EndingStation")));
                double step = a.Dbl("interval", 20);
                for (double s = s0; s <= s1 + 1e-6; s += step) stations.Add(s);
            }
            double left = a.Dbl("left", 20), right = a.Dbl("right", 20);
            var made = new List<string>();
            var warnings = new List<string>();
            foreach (var st in stations)
            {
                try
                {
                    Reflect.CallOut(al, "PointLocation", out var l, st, -left, 0.0, 0.0);
                    Reflect.CallOut(al, "PointLocation", out var m, st, 0.0, 0.0, 0.0);
                    Reflect.CallOut(al, "PointLocation", out var r, st, right, 0.0, 0.0);
                    var pts = new Point2dCollection(new[]
                    {
                        new Point2d((double)l[2], (double)l[3]), new Point2d((double)m[2], (double)m[3]), new Point2d((double)r[2], (double)r[3])
                    });
                    var sl = (ObjectId)Reflect.CallStatic(Reflect.T("SampleLine"), "Create", $"SL-{st:0.00}", gid, pts);
                    made.Add(Ctx.H(sl));
                }
                catch (Exception ex) { warnings.Add($"{st}: {ex.InnerException?.Message ?? ex.Message}"); }
            }
            // switch on sampling for the requested sources
            var wanted = new HashSet<ObjectId>(c.FindMany(a.Strs("sample")));
            var group = c.Open(gid, true);
            var sampled = new List<string>();
            try
            {
                foreach (var src in (System.Collections.IEnumerable)Reflect.Call(group, "GetSectionSources"))
                {
                    var sid = (ObjectId)Reflect.Get(src, "SourceId");
                    if (wanted.Contains(sid)) { Reflect.Set(src, "IsSampled", true); sampled.Add(Ctx.H(sid)); }
                }
            }
            catch (Exception ex) { warnings.Add("sampling: " + ex.Message); }
            return new { group = Ctx.H(gid), sample_lines = made.Count, sampled, warnings };
        }

        /// <summary>Create section views for every sample line in a group, laid out in a grid.</summary>
        [Tool("sections.create_views")]
        public static object CreateViews(Args a, Ctx c)
        {
            var group = c.Open(a.NeedStr("group"));
            var origin = a.Pt("position");
            double dx = a.Dbl("spacing_x", 100), dy = a.Dbl("spacing_y", 60);
            int cols = a.Int("columns", 5);
            var res = new List<string>();
            var warnings = new List<string>();
            int i = 0;
            foreach (var slId in Ctx.SafeIds(Reflect.Call(group, "GetSampleLineIds")))
            {
                var p = new Point3d(origin.X + (i % cols) * dx, origin.Y - (i / cols) * dy, 0);
                try
                {
                    var name = Reflect.Get(c.Open(slId), "Name") as string;
                    res.Add(Ctx.H((ObjectId)Reflect.CallStatic(Reflect.T("SectionView"), "Create", name, slId, p)));
                }
                catch (Exception ex) { warnings.Add(ex.InnerException?.Message ?? ex.Message); }
                i++;
            }
            return new { views = res, warnings };
        }

        [Tool("sections.sample_line_info")]
        public static object SampleInfo(Args a, Ctx c)
        {
            var sl = c.Open(a.NeedStr("sample_line"));
            var d = Props.Pick(sl, "Name", "Station", "Number");
            var secs = new List<object>();
            foreach (var sid in Ctx.SafeIds(Reflect.Call(sl, "GetSectionIds")))
            {
                var s = c.Open(sid);
                var pts = new List<object>();
                try { foreach (var p in (System.Collections.IEnumerable)Reflect.Get(s, "SectionPoints")) pts.Add(Props.Pick(p, "Location", "Offset", "Elevation")); } catch { }
                secs.Add(new { name = Reflect.Get(s, "Name"), points = pts });
            }
            d["sections"] = secs;
            return d;
        }
    }

    /// <summary>Feature lines and grading.</summary>
    public static class GradingHandler
    {
        [Tool("featureline.from_entity")]
        public static object FromEntity(Args a, Ctx c)
        {
            var fl0 = Reflect.T("FeatureLine");
            var id = a.Has("site")
                ? (ObjectId)Reflect.CallStatic(fl0, "Create", a.Str("name", "FL"), c.Find(a.NeedStr("entity")), c.Find(a.Str("site")))
                : (ObjectId)Reflect.CallStatic(fl0, "Create", a.Str("name", "FL"), c.Find(a.NeedStr("entity")));
            var fl = c.Open(id, true);
            if (a.Has("surface")) Reflect.Call(fl, "AssignElevationsFromSurface", c.Find(a.Str("surface")), a.Bool("insert_intermediate", true));
            if (a.Has("style")) try { Reflect.Set(fl, "StyleId", c.StyleId("FeatureLineStyles", a.Str("style"))); } catch { }
            return Props.Pick(fl, "Name", "Handle", "Length2D", "Length3D", "MinElevation", "MaxElevation");
        }

        [Tool("featureline.set_elevations")]
        public static object SetElev(Args a, Ctx c)
        {
            var fl = c.Open(a.NeedStr("featureline"), true);
            if (a.Has("surface")) Reflect.Call(fl, "AssignElevationsFromSurface", c.Find(a.Str("surface")), a.Bool("insert_intermediate", true));
            if (a.Has("raise"))
            {
                // raise every point by the same amount
                var pts = (Autodesk.AutoCAD.Geometry.Point3dCollection)Reflect.Call(fl, "GetPoints", "AllPoints");
                for (int i = 0; i < pts.Count; i++) Reflect.Call(fl, "SetPointElevation", i, pts[i].Z + a.Dbl("raise"));
            }
            var elevs = a.Dbls("point_elevations");
            for (int i = 0; i < elevs.Count; i++) Reflect.Call(fl, "SetPointElevation", i, elevs[i]);
            return Props.Pick(fl, "Name", "MinElevation", "MaxElevation");
        }

        [Tool("site.create")]
        public static object SiteCreate(Args a, Ctx c)
        {
            var id = (ObjectId)Reflect.CallStatic(Reflect.T("Site"), "Create", c.Civ, a.NeedStr("name"));
            return new { handle = Ctx.H(id) };
        }

        /// <summary>Parcels in a site with area/perimeter. (Creating parcels from objects is UI-only in Civil 3D:
        /// use doc.run_command "_CreateParcelFromObjects".)</summary>
        [Tool("parcels.list")]
        public static object ParcelsList(Args a, Ctx c)
        {
            var site = c.Open(a.NeedStr("site"));
            return Ctx.SafeIds(Reflect.Call(site, "GetParcelIds")).Select(id =>
            {
                var d = Props.Pick(c.Open(id), "Name", "Number", "Area", "Perimeter", "Description");
                d["handle"] = Ctx.H(id); return d;
            }).ToList();
        }
    }
}

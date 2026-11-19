using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    /// <summary>Alignments: from polyline, from PI points (tangents + curves + spirals), offset,
    /// entities, stationing, station equations, design speeds, station/offset queries, labels.</summary>
    public static class AlignmentHandler
    {
        static (ObjectId style, ObjectId labels, ObjectId layer, ObjectId site) Common(Args a, Ctx c)
        {
            var style = c.StyleId("AlignmentStyles", a.Str("style"));
            var labels = c.StyleId("LabelSetStyles.AlignmentLabelSetStyles", a.Str("label_set"));
            var layer = a.Has("layer") ? c.EnsureLayer(a.Str("layer")) : c.Db.Clayer;
            var site = a.Has("site") ? c.Find(a.Str("site")) : ObjectId.Null;
            return (style, labels, layer, site);
        }

        static ObjectId CreateEmpty(Args a, Ctx c, string name)
        {
            var (style, labels, layer, site) = Common(a, c);
            var al = Reflect.T("Alignment");
            return (ObjectId)Reflect.CallStatic(al, "Create", c.Civ, name, site, layer, style, labels);
        }

        [Tool("alignment.from_polyline")]
        public static object FromPolyline(Args a, Ctx c)
        {
            var (style, labels, layer, site) = Common(a, c);
            var opts = Activator.CreateInstance(Reflect.T("PolylineOptions"));
            Reflect.Set(opts, "PlineId", c.Find(a.NeedStr("polyline")));
            Reflect.Set(opts, "AddCurvesBetweenTangents", a.Bool("add_curves", false));
            Reflect.Set(opts, "EraseExistingEntities", a.Bool("erase_polyline", false));
            var id = (ObjectId)Reflect.CallStatic(Reflect.T("Alignment"), "Create", c.Civ, opts, a.Str("name", "Alignment"), site, layer, style, labels);
            Finish(a, c, id);
            return Summary(c.Open(id));
        }

        /// <summary>
        /// Layout by PI points: pis [[x,y],...], radii [r per interior PI] (0 = no curve),
        /// spiral_in / spiral_out lengths [per PI] for spiral-curve-spiral (clothoid).
        /// </summary>
        [Tool("alignment.from_pis")]
        public static object FromPis(Args a, Ctx c)
        {
            var pis = a.Pts("pis");
            if (pis.Count < 2) throw new ArgumentException("Need at least 2 PIs.");
            var id = CreateEmpty(a, c, a.Str("name", "Alignment"));
            var al = c.Open(id, true);
            var ents = Reflect.Get(al, "Entities");
            var lineIds = new List<int>();
            for (int i = 0; i < pis.Count - 1; i++)
            {
                var ln = Reflect.Call(ents, "AddFixedLine", pis[i], pis[i + 1]);
                lineIds.Add(Convert.ToInt32(Reflect.Get(ln, "EntityId")));
            }
            var radii = a.Dbls("radii");
            var sin = a.Dbls("spiral_in");
            var sout = a.Dbls("spiral_out");
            var warnings = new List<string>();
            for (int i = 0; i < lineIds.Count - 1; i++)
            {
                double r = i < radii.Count ? radii[i] : a.Dbl("default_radius", 0);
                if (r <= 0) continue;
                double l1 = i < sin.Count ? sin[i] : 0, l2 = i < sout.Count ? sout[i] : l1;
                try
                {
                    if (l1 > 0 || l2 > 0)
                        Reflect.Call(ents, "AddFreeSCS", lineIds[i], lineIds[i + 1], l1, l2, "Length", r, false, "Clothoid");
                    else
                        Reflect.Call(ents, "AddFreeCurve", lineIds[i], lineIds[i + 1], r, "Radius", false, "Compound");
                }
                catch (Exception ex) { warnings.Add($"PI {i + 1}: {ex.InnerException?.Message ?? ex.Message}"); }
            }
            Finish(a, c, id);
            var s = Summary(c.Open(id));
            s["warnings"] = warnings;
            return s;
        }

        static void Finish(Args a, Ctx c, ObjectId id)
        {
            var al = c.Open(id, true);
            if (a.Has("start_station")) Reflect.Set(al, "ReferencePointStation", a.Dbl("start_station"));
            if (a.Has("description")) Reflect.Set(al, "Description", a.Str("description"));
        }

        static Dictionary<string, object> Summary(object al) =>
            Props.Pick(al, "Name", "Handle", "StartingStation", "EndingStation", "Length", "StyleName", "AlignmentType");

        [Tool("alignment.info", ReadOnly = true)]
        public static object Info(Args a, Ctx c)
        {
            var al = c.Open(a.NeedStr("alignment"));
            var d = Summary(al);
            var ents = new List<object>();
            foreach (var e in (System.Collections.IEnumerable)Reflect.Get(al, "Entities"))
                ents.Add(Props.Pick(e, "EntityId", "EntityType", "StartStation", "EndStation", "Length", "Radius", "StartPoint", "EndPoint", "PIPoint", "Direction"));
            d["entities"] = ents;
            d["profiles"] = Ctx.H(Reflect.Call(al, "GetProfileIds") as System.Collections.IEnumerable);
            try { d["station_equations"] = Conv.Plain(Reflect.Get(al, "StationEquations")); } catch { }
            try { d["design_speeds"] = Conv.Plain(Reflect.Get(al, "DesignSpeeds")); } catch { }
            return d;
        }

        [Tool("alignment.station_offset", ReadOnly = true)]
        public static object StationOffset(Args a, Ctx c)
        {
            var al = c.Open(a.NeedStr("alignment"));
            var p = a.Pt("point");
            Reflect.CallOut(al, "StationOffset", out var r, p.X, p.Y, 0.0, 0.0);
            return new { station = r[2], offset = r[3] };
        }

        [Tool("alignment.point_at", ReadOnly = true)]
        public static object PointAt(Args a, Ctx c)
        {
            var al = c.Open(a.NeedStr("alignment"));
            Reflect.CallOut(al, "PointLocation", out var r, a.Dbl("station"), a.Dbl("offset", 0), 0.0, 0.0);
            return new { x = r[2], y = r[3] };
        }

        [Tool("alignment.add_station_equation")]
        public static object StationEq(Args a, Ctx c)
        {
            var al = c.Open(a.NeedStr("alignment"), true);
            Reflect.Call(Reflect.Get(al, "StationEquations"), "Add", a.Dbl("raw_station_back"), a.Dbl("station_ahead"),
                a.Bool("decreasing") ? "Decreasing" : "Increasing");
            return new { ok = true };
        }

        [Tool("alignment.add_design_speed")]
        public static object DesignSpeed(Args a, Ctx c)
        {
            var al = c.Open(a.NeedStr("alignment"), true);
            var ds = Reflect.Call(Reflect.Get(al, "DesignSpeeds"), "Add", a.Dbl("station"), a.Dbl("speed"));
            if (a.Has("comment")) try { Reflect.Set(ds, "Comment", a.Str("comment")); } catch { }
            return new { ok = true };
        }

        [Tool("alignment.offset")]
        public static object Offset(Args a, Ctx c)
        {
            var parent = c.Find(a.NeedStr("alignment"));
            var style = c.StyleId("AlignmentStyles", a.Str("style"));
            var id = (ObjectId)Reflect.CallStatic(Reflect.T("Alignment"), "CreateOffsetAlignment",
                a.Str("name", "Offset"), parent, a.Dbl("offset"), style);
            return Summary(c.Open(id));
        }

        [Tool("alignment.set_labels")]
        public static object SetLabels(Args a, Ctx c)
        {
            var al = c.Open(a.NeedStr("alignment"), true);
            Reflect.Call(al, "ImportLabelSet", c.StyleId("LabelSetStyles.AlignmentLabelSetStyles", a.NeedStr("label_set")));
            return new { ok = true };
        }
    }

    /// <summary>Profiles: surface (EG), layout (FG) by PVIs with vertical curves, profile views, bands, queries.</summary>
    public static class ProfileHandler
    {
        static (ObjectId style, ObjectId labels, ObjectId layer) Common(Args a, Ctx c) =>
            (c.StyleId("ProfileStyles", a.Str("style")),
             c.StyleId("LabelSetStyles.ProfileLabelSetStyles", a.Str("label_set")),
             a.Has("layer") ? c.EnsureLayer(a.Str("layer")) : c.Db.Clayer);

        [Tool("profile.from_surface")]
        public static object FromSurface(Args a, Ctx c)
        {
            var (style, labels, layer) = Common(a, c);
            var id = (ObjectId)Reflect.CallStatic(Reflect.T("Profile"), "CreateFromSurface", a.Str("name", "EG"),
                c.Find(a.NeedStr("alignment")), c.Find(a.NeedStr("surface")), layer, style, labels);
            if (a.Has("offset")) try { Reflect.Set(c.Open(id, true), "Offset", a.Dbl("offset")); } catch { }
            return Summary(c.Open(id));
        }

        /// <summary>pvis: [[station, elevation, curve_length], ...]  curve_length 0 = no vertical curve.</summary>
        [Tool("profile.create_layout")]
        public static object Layout(Args a, Ctx c)
        {
            var (style, labels, layer) = Common(a, c);
            var id = (ObjectId)Reflect.CallStatic(Reflect.T("Profile"), "CreateByLayout", a.Str("name", "FG"),
                c.Find(a.NeedStr("alignment")), layer, style, labels);
            var pr = c.Open(id, true);
            var pviColl = Reflect.Get(pr, "PVIs");
            var rows = a.Arr("pvis").Select(n => (n as System.Text.Json.Nodes.JsonArray)?.Select(Conv.NodeToDouble).ToArray()).ToList();
            var pvis = new List<object>();
            foreach (var r in rows) pvis.Add(Reflect.Call(pviColl, "AddPVI", r[0], r[1]));
            var warnings = new List<string>();
            var ents = Reflect.Get(pr, "Entities");
            for (int i = 1; i < rows.Count - 1; i++)
            {
                if (rows[i].Length < 3 || rows[i][2] <= 0) continue;
                try
                {
                    var type = a.Str("curve_type", "parabola").ToLower();
                    Reflect.Call(ents, "AddFreeSymmetricParabolaByPVIAndCurveLength", pvis[i], rows[i][2]);
                }
                catch (Exception ex) { warnings.Add($"PVI {i}: {ex.InnerException?.Message ?? ex.Message}"); }
            }
            var s = Summary(pr);
            s["warnings"] = warnings;
            return s;
        }

        static Dictionary<string, object> Summary(object pr) =>
            Props.Pick(pr, "Name", "Handle", "StartingStation", "EndingStation", "Length", "ElevationMin", "ElevationMax", "ProfileType", "StyleName");

        [Tool("profile.info", ReadOnly = true)]
        public static object Info(Args a, Ctx c)
        {
            var pr = c.Open(a.NeedStr("profile"));
            var d = Summary(pr);
            var pvis = new List<object>();
            foreach (var p in (System.Collections.IEnumerable)Reflect.Get(pr, "PVIs"))
                pvis.Add(Props.Pick(p, "Station", "Elevation", "GradeIn", "GradeOut", "VerticalCurve"));
            d["pvis"] = pvis;
            return d;
        }

        [Tool("profile.elevation_at", ReadOnly = true)]
        public static object ElevationAt(Args a, Ctx c)
        {
            var pr = c.Open(a.NeedStr("profile"));
            var sts = a.Has("stations") ? a.Dbls("stations") : new List<double> { a.Dbl("station") };
            return sts.Select(s => new { station = s, elevation = CivilCommonHandler.Safe(() => Reflect.Call(pr, "ElevationAt", s)) }).ToList();
        }

        [Tool("profile.add_pvi")]
        public static object AddPvi(Args a, Ctx c)
        {
            var pr = c.Open(a.NeedStr("profile"), true);
            var pvi = Reflect.Call(Reflect.Get(pr, "PVIs"), "AddPVI", a.Dbl("station"), a.Dbl("elevation"));
            if (a.Has("curve_length")) Reflect.Call(Reflect.Get(pr, "Entities"), "AddFreeSymmetricParabolaByPVIAndCurveLength", pvi, a.Dbl("curve_length"));
            return Summary(pr);
        }

        [Tool("profile.view_create")]
        public static object View(Args a, Ctx c)
        {
            var id = (ObjectId)Reflect.CallStatic(Reflect.T("ProfileView"), "Create",
                c.Find(a.NeedStr("alignment")), a.Pt("position"), a.Str("name", "PV"),
                c.StyleId("ProfileViewBandSetStyles", a.Str("band_set")),
                c.StyleId("ProfileViewStyles", a.Str("style")));
            var pv = c.Open(id, true);
            if (a.Has("elevation_min") && a.Has("elevation_max"))
            {
                try
                {
                    Reflect.Set(pv, "ElevationRangeMode", "UserSpecified");
                    Reflect.Set(pv, "ElevationMin", a.Dbl("elevation_min"));
                    Reflect.Set(pv, "ElevationMax", a.Dbl("elevation_max"));
                }
                catch { }
            }
            return new { handle = Ctx.H(id) };
        }
    }
}

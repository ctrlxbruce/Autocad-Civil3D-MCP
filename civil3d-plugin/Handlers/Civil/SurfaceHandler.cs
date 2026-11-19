using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;
using ElevationBand = Autodesk.Civil.DatabaseServices.SurfaceAnalysisElevationData;
using SlopeBand = Autodesk.Civil.DatabaseServices.SurfaceAnalysisSlopeData;

namespace Civil3DMcp.Handlers
{
    /// <summary>
    /// TIN / grid / volume surfaces: create, definitions (point groups, points, breaklines, contours,
    /// boundaries, DEM, .tin import), edits, styles (contours, smoothing, display), analysis, volumes.
    /// </summary>
    public static class SurfaceHandler
    {
        static ObjectIdCollection Ids(Args a, Ctx c, string key = "handles") => new ObjectIdCollection(c.FindMany(a.Strs(key)).ToArray());

        static void ApplyStyle(Args a, Ctx c, ObjectId id)
        {
            if (a.Has("style")) Reflect.Set(c.Open(id, true), "StyleId", c.StyleId("SurfaceStyles", a.Str("style")));
            if (a.Has("layer")) { c.EnsureLayer(a.Str("layer")); ((Entity)c.Open(id, true)).Layer = a.Str("layer"); }
            if (a.Has("description")) Reflect.Set(c.Open(id, true), "Description", a.Str("description"));
        }

        [Tool("surface.create")]
        public static object Create(Args a, Ctx c)
        {
            var type = a.Str("type", "tin").ToLowerInvariant();   // tin | grid
            var name = a.NeedStr("name");
            ObjectId id = type == "grid"
                ? (ObjectId)Reflect.CallStatic(Reflect.T("GridSurface"), "Create", c.Db, name)
                : (ObjectId)Reflect.CallStatic(Reflect.T("TinSurface"), "Create", c.Db, name);
            ApplyStyle(a, c, id);
            return new { handle = Ctx.H(id), name };
        }

        [Tool("surface.import_tin_file")]
        public static object ImportTin(Args a, Ctx c)
        {
            var id = (ObjectId)Reflect.CallStatic(Reflect.T("TinSurface"), "CreateFromTin", c.Db, a.NeedStr("path"));
            if (a.Has("name")) Reflect.Set(c.Open(id, true), "Name", a.Str("name"));
            ApplyStyle(a, c, id);
            return new { handle = Ctx.H(id) };
        }

        [Tool("surface.add_point_groups")]
        public static object AddPointGroups(Args a, Ctx c)
        {
            var s = c.Open(a.NeedStr("surface"), true);
            var def = Reflect.Get(s, "PointGroupsDefinition");
            foreach (var g in c.FindMany(a.Strs("groups"))) Reflect.Call(def, "AddPointGroup", g);
            Rebuild(s);
            return Info(s);
        }

        [Tool("surface.add_points")]
        public static object AddPoints(Args a, Ctx c)
        {
            var s = c.Open(a.NeedStr("surface"), true);
            Reflect.Call(s, "AddVertices", new Point3dCollection(a.Pts("points").ToArray()));
            return Info(s);
        }

        /// <summary>type: standard | proximity | wall | farfield. Uses polylines / feature lines / 3D polylines.</summary>
        [Tool("surface.add_breaklines")]
        public static object AddBreaklines(Args a, Ctx c)
        {
            var s = c.Open(a.NeedStr("surface"), true);
            var def = Reflect.Get(s, "BreaklinesDefinition");
            var ids = Ids(a, c);
            double mid = a.Dbl("mid_ordinate", 0.1), max = a.Dbl("max_distance", 15), wd = a.Dbl("weeding_distance", 0), wa = a.Dbl("weeding_angle", 0);
            switch (a.Str("type", "standard").ToLowerInvariant())
            {
                case "proximity": Reflect.Call(def, "AddProximityBreaklines", ids, mid); break;
                case "farfield": Reflect.Call(def, "AddNonDestructiveBreaklines", ids, mid); break;
                default: Reflect.Call(def, "AddStandardBreaklines", ids, mid, max, wd, wa); break;
            }
            Rebuild(s);
            return Info(s);
        }

        /// <summary>Build from existing contour polylines (with elevation).</summary>
        [Tool("surface.add_contours")]
        public static object AddContours(Args a, Ctx c)
        {
            var s = c.Open(a.NeedStr("surface"), true);
            var def = Reflect.Get(s, "ContoursDefinition");
            Reflect.Call(def, "AddContours", Ids(a, c),
                a.Dbl("mid_ordinate", 0.1), a.Dbl("max_distance", 15), a.Dbl("weeding_distance", 0), a.Dbl("weeding_angle", 0));
            Rebuild(s);
            return Info(s);
        }

        /// <summary>type: Outer | Hide | Show | DataClip</summary>
        [Tool("surface.add_boundary")]
        public static object AddBoundary(Args a, Ctx c)
        {
            var s = c.Open(a.NeedStr("surface"), true);
            var def = Reflect.Get(s, "BoundariesDefinition");
            Reflect.Call(def, "AddBoundaries", Ids(a, c), a.Dbl("mid_ordinate", 0.1), a.Str("type", "Outer"), a.Bool("non_destructive", true));
            Rebuild(s);
            return Info(s);
        }

        [Tool("surface.add_dem")]
        public static object AddDem(Args a, Ctx c)
        {
            var s = c.Open(a.NeedStr("surface"), true);
            Reflect.Call(Reflect.Get(s, "DEMFilesDefinition"), "AddDEMFile", a.NeedStr("path"));
            Rebuild(s);
            return Info(s);
        }

        /// <summary>Simple edits. op: raise (amount) | paste (source) | delete_line (point) | swap_edge (point) |
        /// add_line (start,end) | delete_point (point) | add_point (point) | smooth (NNI) | simplify (percent).</summary>
        [Tool("surface.edit")]
        public static object Edit(Args a, Ctx c)
        {
            var s = c.Open(a.NeedStr("surface"), true);
            switch (a.NeedStr("op").ToLowerInvariant())
            {
                case "raise": Reflect.Call(s, "RaiseSurface", a.Dbl("amount")); break;
                case "paste": Reflect.Call(s, "PasteSurface", c.Find(a.NeedStr("source"))); break;
                case "add_point": Reflect.Call(s, "AddVertex", a.Pt("point")); break;
                case "delete_point":
                    var v = Reflect.Call(s, "FindVertexAtXY", a.Pt("point").X, a.Pt("point").Y);
                    Reflect.Call(s, "DeleteVertex", v); break;
                case "add_line":
                    var v1 = Reflect.Call(s, "FindVertexAtXY", a.Pt("start").X, a.Pt("start").Y);
                    var v2 = Reflect.Call(s, "FindVertexAtXY", a.Pt("end").X, a.Pt("end").Y);
                    Reflect.Call(s, "AddLine", v1, v2); break;
                case "delete_line":
                    var edge = Reflect.Call(s, "FindEdgeAtXY", a.Pt("point").X, a.Pt("point").Y);
                    Reflect.Call(s, "DeleteLine", edge); break;
                case "swap_edge":
                    var e2 = Reflect.Call(s, "FindEdgeAtXY", a.Pt("point").X, a.Pt("point").Y);
                    Reflect.Call(s, "SwapEdge", e2); break;
                case "smooth":
                    var opts = Activator.CreateInstance(Reflect.T("SurfacePointOutputOptions"));
                    Reflect.Set(opts, "OutputLocations", a.Str("locations", "GridBased"));
                    if (a.Has("grid_spacing")) { Reflect.Set(opts, "GridSpacingX", a.Dbl("grid_spacing")); Reflect.Set(opts, "GridSpacingY", a.Dbl("grid_spacing")); }
                    Reflect.Call(s, a.Str("method", "nni") == "kriging" ? "SmoothSurfaceByKriging" : "SmoothSurfaceByNNI", opts); break;
                case "simplify":
                    var so = Activator.CreateInstance(Reflect.T("SurfaceSimplifyOptions"), Reflect.Enum("SurfaceSimplifyType", a.Str("method", "PointRemoval")));
                    Reflect.Set(so, "UseMaximumChangeInElevation", false);
                    Reflect.Set(so, "UsePercentageOfPoints", true);
                    Reflect.Set(so, "PercentageOfPoints", a.Dbl("percent", 0.5));
                    Reflect.Call(s, "SimplifySurface", so); break;
                default: throw new ArgumentException("op not recognised.");
            }
            Rebuild(s);
            return Info(s);
        }

        /// <summary>Build a TIN volume surface (comparison) and report cut/fill.</summary>
        [Tool("surface.volume")]
        public static object Volume(Args a, Ctx c)
        {
            ObjectId vid;
            if (a.Has("volume_surface")) vid = c.Find(a.Str("volume_surface"));
            else vid = (ObjectId)Reflect.CallStatic(Reflect.T("TinVolumeSurface"), "Create",
                     a.Str("name", "VOL"), c.Find(a.NeedStr("base")), c.Find(a.NeedStr("comparison")));
            var vs = c.Open(vid, true);
            if (a.Has("cut_factor")) Reflect.SetPath(vs, "CutFactor", a.Dbl("cut_factor"));
            if (a.Has("fill_factor")) Reflect.SetPath(vs, "FillFactor", a.Dbl("fill_factor"));
            var vol = Reflect.Call(vs, "GetVolumeProperties");
            return new { handle = Ctx.H(vid), volumes = Props.GetAll(vol) };
        }

        [Tool("surface.info", ReadOnly = true)]
        public static object InfoTool(Args a, Ctx c) => Info(c.Open(a.NeedStr("surface")), full: true);

        static object Info(object s, bool full = false)
        {
            var d = new Dictionary<string, object> { ["name"] = Reflect.Get(s, "Name") };
            try { d["general"] = Props.GetAll(Reflect.Call(s, "GetGeneralProperties")); } catch { }
            if (full)
            {
                try { d["terrain"] = Props.GetAll(Reflect.Call(s, "GetTerrainProperties")); } catch { }
                try { d["tin"] = Props.GetAll(Reflect.Call(s, "GetTinProperties")); } catch { }
                try { d["style"] = Conv.Plain(Reflect.Get(s, "StyleName")); } catch { }
            }
            return d;
        }

        static void Rebuild(object s)
        {
            try { if (Reflect.Get(s, "IsOutOfDate") is bool b && b) Reflect.Call(s, "Rebuild"); } catch { }
        }

        [Tool("surface.elevation_at", ReadOnly = true)]
        public static object ElevationAt(Args a, Ctx c)
        {
            var s = c.Open(a.NeedStr("surface"));
            var pts = a.Has("points") ? a.Pts("points") : new List<Point3d> { a.Pt("point") };
            return pts.Select(p =>
            {
                try { return (object)new { x = p.X, y = p.Y, z = Reflect.Call(s, "FindElevationAtXY", p.X, p.Y) }; }
                catch { return new { x = p.X, y = p.Y, z = (object)null }; }
            }).ToList();
        }

        /// <summary>Extract contours (or borders) as AutoCAD polylines.</summary>
        [Tool("surface.extract_contours")]
        public static object Extract(Args a, Ctx c)
        {
            var s = c.Open(a.NeedStr("surface"), true);
            object ids;
            switch (a.Str("what", "contours").ToLowerInvariant())
            {
                case "major": ids = Reflect.Call(s, "ExtractMajorContours", "Plan"); break;
                case "minor": ids = Reflect.Call(s, "ExtractMinorContours", "Plan"); break;
                case "border": ids = Reflect.Call(s, "ExtractBorder", "Plan"); break;
                default:
                    ids = a.Has("smoothing")
                        ? Reflect.Call(s, "ExtractContours", a.Dbl("interval", 1), a.Str("smoothing"), a.Int("smoothness", 5))
                        : Reflect.Call(s, "ExtractContours", a.Dbl("interval", 1));
                    break;
            }
            var handles = Ctx.H((System.Collections.IEnumerable)ids);
            if (a.Has("layer")) foreach (var h in handles) { c.EnsureLayer(a.Str("layer")); ((Entity)c.Open(h, true)).Layer = a.Str("layer"); }
            return new { count = handles.Count, handles };
        }

        /// <summary>
        /// Contour settings on a SURFACE STYLE: major/minor interval, base elevation, smoothing on/off,
        /// smoothing type (AddVertices | SplineCurve), smoothing amount 1-10, and which components show.
        /// </summary>
        [Tool("surface.style_contours")]
        public static object StyleContours(Args a, Ctx c)
        {
            var st = c.Open(c.StyleId("SurfaceStyles", a.NeedStr("style")), true);
            var cs = Reflect.Get(st, "ContourStyle");
            var report = new Dictionary<string, string>();
            void Try(string key, params string[] props)
            {
                if (!a.Has(key)) return;
                foreach (var p in props)
                    try { Reflect.Set(cs, p, a.Node(key), c); report[key] = "ok (" + p + ")"; return; } catch (Exception ex) { report[key] = ex.InnerException?.Message ?? ex.Message; }
            }
            Try("major_interval", "MajorContourInterval");
            Try("minor_interval", "MinorContourInterval");
            Try("base_elevation", "BaseElevationInterval");
            Try("smooth", "SmoothContours");
            Try("smoothing_type", "SmoothingType");
            Try("smoothing_amount", "SmoothingFactor");
            Try("depression_ticks", "DisplayDepressions");
            var show = a.Obj("show");   // {"MajorContour":true,"MinorContour":true,"Triangles":false,"Points":false,"Boundary":true}
            if (show != null)
                foreach (var kv in show)
                    try { Reflect.Set(Reflect.Call(st, "GetDisplayStylePlan", kv.Key), "Visible", Conv.To(kv.Value, typeof(bool), c)); report["show." + kv.Key] = "ok"; }
                    catch (Exception ex) { report["show." + kv.Key] = ex.InnerException?.Message ?? ex.Message; }
            return report;
        }

        /// <summary>Elevation / slope analysis ranges on a surface (banding).</summary>
        /// <summary>
        /// Elevation / slope analysis: splits the surface's range into equal bands, saves them on the surface
        /// (shown when the surface style displays Elevations / Slopes) and returns them.
        /// The Civil 3D .NET API has no "calculate regions" call, so the bands are built here.
        /// watershed: returns the surface's existing watershed data.
        /// </summary>
        [Tool("surface.analysis")]
        public static object Analysis(Args a, Ctx c)
        {
            var kind = a.Str("kind", "elevation").Trim().ToLowerInvariant().TrimEnd('s');
            if (kind != "elevation" && kind != "slope" && kind != "watershed")
                throw new ArgumentException($"Unknown kind '{a.Str("kind")}'. Use elevation, slope or watershed.");
            int n = a.Int("ranges", 8);
            if (n < 1 || n > 64) throw new ArgumentException("ranges must be between 1 and 64.");

            var s = c.Open(a.NeedStr("surface"), true) as CivilSurface
                ?? throw new ArgumentException($"'{a.Str("surface")}' is not a surface.");
            var an = s.Analysis;

            if (kind == "watershed") return Conv.Plain(an.GetWatershedData());

            if (kind == "elevation")
            {
                var gp = s.GetGeneralProperties();
                var bands = Bands(gp.MinimumElevation, gp.MaximumElevation, n);
                an.SetElevationData(bands.Select((b, i) => new ElevationBand(b.lo, b.hi, Ramp(i, bands.Count))).ToArray());
                return new
                {
                    kind,
                    surface = s.Name,
                    ranges = an.GetElevationData()
                        .Select(d => new { min = d.MinimumElevation, max = d.MaximumElevation, color = d.Scheme.ColorNameForDisplay })
                        .ToList(),
                    note = "Shown in the drawing when the surface style displays Elevations.",
                };
            }

            // Slope: grades are fractions (0.05 = 5 %), like the surface's terrain properties.
            var tp = Reflect.Call(s, "GetTerrainProperties");
            double smin = Convert.ToDouble(Reflect.Get(tp, "MinimumGradeOrSlope"));
            double smax = Convert.ToDouble(Reflect.Get(tp, "MaximumGradeOrSlope"));
            var slopeBands = Bands(smin, smax, n);
            an.SetSlopeData(slopeBands.Select((b, i) => new SlopeBand(b.lo, b.hi, Ramp(i, slopeBands.Count))).ToArray());
            return new
            {
                kind,
                surface = s.Name,
                ranges = an.GetSlopeData()
                    .Select(d => new
                    {
                        min = d.MinimumSlope,
                        max = d.MaximumSlope,
                        min_percent = Math.Round(d.MinimumSlope * 100, 2),
                        max_percent = Math.Round(d.MaximumSlope * 100, 2),
                        color = d.Scheme.ColorNameForDisplay,
                    })
                    .ToList(),
                note = "Slopes are fractions (0.05 = 5 %). Shown in the drawing when the surface style displays Slopes.",
            };
        }

        static List<(double lo, double hi)> Bands(double lo, double hi, int n)
        {
            if (double.IsNaN(lo) || double.IsNaN(hi) || hi < lo)
                throw new InvalidOperationException("The surface has no valid range yet. Add points, contours or breaklines first.");
            if (hi == lo) n = 1;
            double step = (hi - lo) / n;
            var list = new List<(double lo, double hi)>();
            for (int i = 0; i < n; i++) list.Add((lo + i * step, i == n - 1 ? hi : lo + (i + 1) * step));
            return list;
        }

        // Colour ramp from blue (low) through green to red (high).
        static AcColor Ramp(int i, int n)
        {
            double t = n <= 1 ? 0 : (double)i / (n - 1);
            byte r = (byte)Math.Round(255 * t);
            byte g = (byte)Math.Round(255 * (1 - Math.Abs(2 * t - 1)));
            byte b = (byte)Math.Round(255 * (1 - t));
            return AcColor.FromRgb(r, g, b);
        }
    }
}

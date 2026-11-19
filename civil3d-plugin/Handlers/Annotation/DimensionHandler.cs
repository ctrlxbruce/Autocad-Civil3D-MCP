using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    /// <summary>All dimension types, dimension styles (arrows, text, lines, units), multileaders.</summary>
    public static class DimensionHandler
    {
        static object Out(ObjectId id) => new { handle = Ctx.H(id) };
        static string Txt(Args a) => a.Str("text_override", "");   // "" = measured value, "<> m" = measured + suffix

        static ObjectId Finish(Dimension d, Args a, Ctx c)
        {
            var id = c.Add(d, a);
            if (a.Has("props")) Props.SetMany(d, a.Obj("props"), c);
            return id;
        }

        [Tool("dim.linear")]
        public static object Linear(Args a, Ctx c)
        {
            // rotation 0 = horizontal, 90 = vertical
            var d = new RotatedDimension(a.Deg("rotation"), a.Pt("p1"), a.Pt("p2"), a.Pt("dim_line_point"), Txt(a), c.DimStyleId(a.Str("style")));
            return Out(Finish(d, a, c));
        }

        [Tool("dim.aligned")]
        public static object Aligned(Args a, Ctx c) =>
            Out(Finish(new AlignedDimension(a.Pt("p1"), a.Pt("p2"), a.Pt("dim_line_point"), Txt(a), c.DimStyleId(a.Str("style"))), a, c));

        [Tool("dim.angular")]
        public static object Angular(Args a, Ctx c) =>
            Out(Finish(new Point3AngularDimension(a.Pt("vertex"), a.Pt("p1"), a.Pt("p2"), a.Pt("arc_point"), Txt(a), c.DimStyleId(a.Str("style"))), a, c));

        [Tool("dim.angular_2lines")]
        public static object Angular2(Args a, Ctx c) =>
            Out(Finish(new LineAngularDimension2(a.Pt("line1_start"), a.Pt("line1_end"), a.Pt("line2_start"), a.Pt("line2_end"), a.Pt("arc_point"), Txt(a), c.DimStyleId(a.Str("style"))), a, c));

        static (Point3d ctr, double r) CircleOf(Args a, Ctx c)
        {
            if (a.Has("handle"))
            {
                var o = c.Open(a.Str("handle"));
                if (o is Circle ci) return (ci.Center, ci.Radius);
                if (o is Arc ar) return (ar.Center, ar.Radius);
                throw new ArgumentException("handle must be a circle or arc.");
            }
            return (a.Pt("center"), a.Dbl("radius"));
        }

        [Tool("dim.radius")]
        public static object Radius(Args a, Ctx c)
        {
            var (ctr, r) = CircleOf(a, c);
            double ang = a.Deg("angle", 45);
            var chord = new Point3d(ctr.X + r * Math.Cos(ang), ctr.Y + r * Math.Sin(ang), ctr.Z);
            return Out(Finish(new RadialDimension(ctr, chord, a.Dbl("leader_length", r * 0.5), Txt(a), c.DimStyleId(a.Str("style"))), a, c));
        }

        [Tool("dim.diameter")]
        public static object Diameter(Args a, Ctx c)
        {
            var (ctr, r) = CircleOf(a, c);
            double ang = a.Deg("angle", 45);
            var p1 = new Point3d(ctr.X + r * Math.Cos(ang), ctr.Y + r * Math.Sin(ang), ctr.Z);
            var p2 = new Point3d(ctr.X - r * Math.Cos(ang), ctr.Y - r * Math.Sin(ang), ctr.Z);
            return Out(Finish(new DiametricDimension(p1, p2, a.Dbl("leader_length", r * 0.5), Txt(a), c.DimStyleId(a.Str("style"))), a, c));
        }

        [Tool("dim.arc_length")]
        public static object ArcLength(Args a, Ctx c)
        {
            var arc = (Arc)c.Open(a.NeedStr("handle"));
            var mid = arc.GetPointAtDist(arc.Length / 2);
            var dir = (mid - arc.Center).GetNormal();
            var arcPt = a.Has("arc_point") ? a.Pt("arc_point") : mid + dir * a.Dbl("offset", arc.Radius * 0.2);
            return Out(Finish(new ArcDimension(arc.Center, arc.StartPoint, arc.EndPoint, arcPt, Txt(a), c.DimStyleId(a.Str("style"))), a, c));
        }

        [Tool("dim.ordinate")]
        public static object Ordinate(Args a, Ctx c) =>
            Out(Finish(new OrdinateDimension(a.Str("axis", "x").ToLower() == "x", a.Pt("feature_point"), a.Pt("leader_end"), Txt(a), c.DimStyleId(a.Str("style"))), a, c));

        /// <summary>Dimension every segment of a polyline (lengths along each segment).</summary>
        [Tool("dim.polyline_segments")]
        public static object PolylineSegments(Args a, Ctx c)
        {
            var pl = (Polyline)c.Open(a.NeedStr("handle"));
            double off = a.Dbl("offset", 2);
            var res = new List<string>();
            int n = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
            for (int i = 0; i < n; i++)
            {
                if (pl.GetSegmentType(i) != SegmentType.Line) continue;
                var seg = pl.GetLineSegmentAt(i);
                var mid = seg.MidPoint;
                var nrm = seg.Direction.GetPerpendicularVector().GetNormal() * off;
                var d = new AlignedDimension(seg.StartPoint, seg.EndPoint, mid + nrm, "", c.DimStyleId(a.Str("style")));
                res.Add(Ctx.H(Finish(d, a, c)));
            }
            return new { handles = res };
        }

        // ------------------------------------------------------------------ styles
        static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
        {
            ["arrow_size"] = "Dimasz", ["text_height"] = "Dimtxt", ["text_gap"] = "Dimgap",
            ["ext_offset"] = "Dimexo", ["ext_extend"] = "Dimexe", ["dim_line_extend"] = "Dimdle",
            ["baseline_spacing"] = "Dimdli", ["decimals"] = "Dimdec", ["angle_decimals"] = "Dimadec",
            ["overall_scale"] = "Dimscale", ["linear_scale"] = "Dimlfac", ["text_vertical"] = "Dimtad",
            ["text_horizontal"] = "Dimjust", ["text_inside_horizontal"] = "Dimtih", ["text_outside_horizontal"] = "Dimtoh",
            ["line_color"] = "Dimclrd", ["ext_color"] = "Dimclre", ["text_color"] = "Dimclrt",
            ["line_weight"] = "Dimlwd", ["ext_weight"] = "Dimlwe", ["units"] = "Dimlunit", ["angle_units"] = "Dimaunit",
            ["suffix"] = "Dimpost", ["round"] = "Dimrnd", ["center_mark"] = "Dimcen", ["tolerance"] = "Dimtol",
            ["tol_plus"] = "Dimtp", ["tol_minus"] = "Dimtm", ["suppress_zeros"] = "Dimzin", ["fit"] = "Dimatfit",
            ["text_movement"] = "Dimtmove", ["force_line_inside"] = "Dimtofl", ["suppress_ext1"] = "Dimse1",
            ["suppress_ext2"] = "Dimse2", ["fixed_ext_length"] = "DimfxlenOn", ["ext_length"] = "Dimfxlen",
            ["decimal_separator"] = "Dimdsep", ["text_fill_color"] = "Dimtfillclr", ["text_fill"] = "Dimtfill",
        };

        /// <summary>
        /// Create or update a dimension style. Friendly keys (see Alias) or raw DIM variable names
        /// (Dimasz, Dimtxt, Dimtad, ...). Arrows: arrow / arrow1 / arrow2 / leader_arrow with names like
        /// "ClosedFilled", "_ARCHTICK", "_OBLIQUE", "_DOT", "_OPEN", "_OPEN30", "_NONE", "_DOTSMALL", "_INTEGRAL".
        /// </summary>
        [Tool("styles.dim_style")]
        public static object DimStyle(Args a, Ctx c)
        {
            var name = a.NeedStr("name");
            var tbl = c.Open<DimStyleTable>(c.Db.DimStyleTableId);
            DimStyleTableRecord rec;
            if (tbl.Has(name)) rec = c.Open<DimStyleTableRecord>(tbl[name], true);
            else
            {
                rec = c.Db.GetDimstyleData();   // start from current settings
                rec.Name = name;
                tbl.UpgradeOpen();
                tbl.Add(rec);
                c.Tr.AddNewlyCreatedDBObject(rec, true);
            }
            var report = new Dictionary<string, string>();
            var settings = a.Obj("settings") ?? new System.Text.Json.Nodes.JsonObject();
            foreach (var kv in settings)
            {
                try
                {
                    var key = kv.Key.ToLowerInvariant();
                    if (key == "arrow") { var id = ArrowId(c, Conv.NodeToString(kv.Value)); rec.Dimblk = id; rec.Dimsah = false; }
                    else if (key == "arrow1") { rec.Dimsah = true; rec.Dimblk1 = ArrowId(c, Conv.NodeToString(kv.Value)); }
                    else if (key == "arrow2") { rec.Dimsah = true; rec.Dimblk2 = ArrowId(c, Conv.NodeToString(kv.Value)); }
                    else if (key == "leader_arrow") rec.Dimldrblk = ArrowId(c, Conv.NodeToString(kv.Value));
                    else if (key == "text_style") rec.Dimtxsty = c.TextStyleId(Conv.NodeToString(kv.Value));
                    else if (key == "linetype") rec.Dimltype = c.EnsureLinetype(Conv.NodeToString(kv.Value));
                    else Reflect.Set(rec, Alias.TryGetValue(kv.Key, out var real) ? real : kv.Key, kv.Value, c);
                    report[kv.Key] = "ok";
                }
                catch (Exception ex) { report[kv.Key] = "ERROR: " + (ex.InnerException?.Message ?? ex.Message); }
            }
            if (a.Bool("make_current", true))
            {
                c.Db.Dimstyle = rec.ObjectId;
                c.Db.SetDimstyleData(rec);
            }
            return new { name, handle = Ctx.H(rec.ObjectId), settings = report };
        }

        [Tool("styles.list_dim_styles", ReadOnly = true)]
        public static object ListDimStyles(Args a, Ctx c) =>
            c.Open<DimStyleTable>(c.Db.DimStyleTableId).Cast<ObjectId>().Select(id => c.Open<DimStyleTableRecord>(id))
             .Select(r => new { name = r.Name, arrow_size = r.Dimasz, text_height = r.Dimtxt, scale = r.Dimscale, decimals = r.Dimdec, current = r.ObjectId == c.Db.Dimstyle })
             .ToList();

        [Tool("styles.dim_style_get", ReadOnly = true)]
        public static object DimStyleGet(Args a, Ctx c) => Props.GetAll(c.Open(c.DimStyleId(a.NeedStr("name"))));

        /// <summary>Arrow blocks only exist after first use; setting DIMBLK makes AutoCAD create them.</summary>
        internal static ObjectId ArrowId(Ctx c, string name)
        {
            if (string.IsNullOrEmpty(name) || name.Replace("_", "").Equals("ClosedFilled", StringComparison.OrdinalIgnoreCase) || name == ".")
                return ObjectId.Null;
            var blk = name.StartsWith("_") ? name : "_" + name;
            var bt = c.Open<BlockTable>(c.Db.BlockTableId);
            if (!bt.Has(blk))
            {
                var old = Application.GetSystemVariable("DIMBLK") as string;
                Application.SetSystemVariable("DIMBLK", blk);
                Application.SetSystemVariable("DIMBLK", string.IsNullOrEmpty(old) ? "." : old);
                bt = c.Open<BlockTable>(c.Db.BlockTableId);
            }
            if (!bt.Has(blk)) throw new ArgumentException($"Arrow '{name}' unknown.");
            return bt[blk];
        }

        // ------------------------------------------------------------------ multileaders
        [Tool("annotation.mleader")]
        public static object AddMLeader(Args a, Ctx c)
        {
            var ml = new MLeader();
            ml.SetDatabaseDefaults(c.Db);
            if (a.Has("style")) ml.MLeaderStyle = MLeaderStyleId(c, a.Str("style"), false);
            ml.ContentType = ContentType.MTextContent;
            var mt = new MText();
            mt.SetDatabaseDefaults(c.Db);
            mt.Contents = a.NeedStr("text");
            mt.Location = a.Pt("text_point");
            if (a.Has("text_height")) mt.TextHeight = a.Dbl("text_height");
            ml.MText = mt;
            var arrows = a.Has("arrow_points") ? a.Pts("arrow_points") : new List<Point3d> { a.Pt("arrow_point") };
            foreach (var p in arrows)
            {
                int idx = ml.AddLeaderLine(a.Pt("text_point"));
                ml.AddFirstVertex(idx, p);
            }
            return Out(c.Add(ml, a));
        }

        static ObjectId MLeaderStyleId(Ctx c, string name, bool create)
        {
            var dict = c.Open<DBDictionary>(c.Db.MLeaderStyleDictionaryId);
            if (dict.Contains(name)) return dict.GetAt(name);
            if (!create) throw new ArgumentException($"MLeader style '{name}' not found.");
            var st = new MLeaderStyle();
            var id = st.PostMLeaderStyleToDb(c.Db, name);
            c.Tr.AddNewlyCreatedDBObject(st, true);
            return id;
        }

        [Tool("styles.mleader_style")]
        public static object MLeaderStyleSet(Args a, Ctx c)
        {
            var id = MLeaderStyleId(c, a.NeedStr("name"), true);
            var st = c.Open<MLeaderStyle>(id, true);
            if (a.Has("arrow")) st.ArrowSymbolId = ArrowId(c, a.Str("arrow"));
            if (a.Has("arrow_size")) st.ArrowSize = a.Dbl("arrow_size");
            if (a.Has("text_height")) st.TextHeight = a.Dbl("text_height");
            if (a.Has("text_style")) st.TextStyleId = c.TextStyleId(a.Str("text_style"));
            if (a.Has("landing_gap")) st.LandingGap = a.Dbl("landing_gap");
            if (a.Has("dogleg_length")) { st.EnableDogleg = true; st.DoglegLength = a.Dbl("dogleg_length"); }
            if (a.Has("leader_color")) st.LeaderLineColor = Conv.ToColor(a.Node("leader_color"));
            if (a.Has("text_color")) st.TextColor = Conv.ToColor(a.Node("text_color"));
            if (a.Has("leader_type")) st.LeaderLineType = (LeaderType)Enum.Parse(typeof(LeaderType), a.Str("leader_type"), true); // StraightLeader | SplineLeader | InVisibleLeader
            if (a.Has("max_points")) st.MaxLeaderSegmentsPoints = a.Int("max_points");
            if (a.Has("scale")) st.Scale = a.Dbl("scale");
            if (a.Has("props")) Props.SetMany(st, a.Obj("props"), c);
            if (a.Bool("make_current")) c.Db.MLeaderstyle = id;
            return new { handle = Ctx.H(id) };
        }
    }
}

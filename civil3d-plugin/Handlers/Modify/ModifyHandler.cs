using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    /// <summary>Move, copy, rotate, scale, mirror, offset, explode, join, arrays, match properties.
    /// Trim / extend / fillet / chamfer / stretch / break: use doc.run_command (they need picks).</summary>
    public static class ModifyHandler
    {
        static IEnumerable<Entity> Ents(Args a, Ctx c, bool write = true) =>
            c.FindMany(a.Strs("handles")).Select(id => (Entity)c.Open(id, write));

        static int Xform(Args a, Ctx c, Matrix3d m)
        {
            int n = 0;
            foreach (var e in Ents(a, c)) { e.TransformBy(m); n++; }
            return n;
        }

        [Tool("modify.move")]
        public static object Move(Args a, Ctx c) =>
            new { moved = Xform(a, c, Matrix3d.Displacement(a.Pt("to") - a.Pt("from", Point3d.Origin))) };

        [Tool("modify.rotate")]
        public static object Rotate(Args a, Ctx c) =>
            new { rotated = Xform(a, c, Matrix3d.Rotation(a.Deg("angle"), Vector3d.ZAxis, a.Pt("base"))) };

        [Tool("modify.scale")]
        public static object Scale(Args a, Ctx c) =>
            new { scaled = Xform(a, c, Matrix3d.Scaling(a.Dbl("factor"), a.Pt("base"))) };

        static List<string> CopyWith(Args a, Ctx c, Matrix3d m)
        {
            var space = c.CurrentSpace();
            var outIds = new List<string>();
            foreach (var e in Ents(a, c, false))
            {
                var copy = (Entity)e.GetTransformedCopy(m);
                outIds.Add(Ctx.H(space.AppendEntity(copy)));
                c.Tr.AddNewlyCreatedDBObject(copy, true);
            }
            return outIds;
        }

        [Tool("modify.copy")]
        public static object Copy(Args a, Ctx c) =>
            new { handles = CopyWith(a, c, Matrix3d.Displacement(a.Pt("to") - a.Pt("from", Point3d.Origin))) };

        [Tool("modify.mirror")]
        public static object Mirror(Args a, Ctx c)
        {
            var m = Matrix3d.Mirroring(new Line3d(a.Pt("p1"), a.Pt("p2")));
            if (a.Bool("erase_source")) return new { mirrored = Xform(a, c, m) };
            return new { handles = CopyWith(a, c, m) };
        }

        [Tool("modify.offset")]
        public static object Offset(Args a, Ctx c)
        {
            var cv = (Curve)c.Open(a.NeedStr("handle"));
            var space = c.CurrentSpace();
            var res = new List<string>();
            foreach (DBObject o in cv.GetOffsetCurves(a.Dbl("distance")))
            {
                var e = (Entity)o;
                res.Add(Ctx.H(space.AppendEntity(e)));
                c.Tr.AddNewlyCreatedDBObject(e, true);
                if (a.Has("layer")) { c.EnsureLayer(a.Str("layer")); e.Layer = a.Str("layer"); }
            }
            return new { handles = res, note = "Positive distance offsets to one side, negative to the other." };
        }

        [Tool("modify.explode")]
        public static object Explode(Args a, Ctx c)
        {
            var res = new List<string>();
            var space = c.CurrentSpace();
            foreach (var e in Ents(a, c))
            {
                var parts = new DBObjectCollection();
                e.Explode(parts);
                foreach (DBObject p in parts)
                {
                    res.Add(Ctx.H(space.AppendEntity((Entity)p)));
                    c.Tr.AddNewlyCreatedDBObject(p, true);
                }
                e.Erase();
            }
            return new { handles = res };
        }

        [Tool("modify.join")]
        public static object Join(Args a, Ctx c)
        {
            var target = (Entity)c.Open(a.NeedStr("target"), true);
            var others = c.FindMany(a.Strs("handles")).Select(id => (Entity)c.Open(id, true)).ToArray();
            target.JoinEntities(others);
            foreach (var o in others) o.Erase();
            return new { handle = Ctx.H(target.ObjectId) };
        }

        [Tool("modify.array_rect")]
        public static object ArrayRect(Args a, Ctx c)
        {
            int rows = a.Int("rows", 1), cols = a.Int("columns", 1);
            double dx = a.Dbl("column_spacing"), dy = a.Dbl("row_spacing");
            var res = new List<string>();
            for (int r = 0; r < rows; r++)
                for (int k = 0; k < cols; k++)
                    if (r != 0 || k != 0) res.AddRange(CopyWith(a, c, Matrix3d.Displacement(new Vector3d(k * dx, r * dy, 0))));
            return new { handles = res };
        }

        [Tool("modify.array_polar")]
        public static object ArrayPolar(Args a, Ctx c)
        {
            int n = a.Int("count", 4);
            double fill = a.Deg("fill_angle", 360);
            double step = Math.Abs(fill - 2 * Math.PI) < 1e-9 ? fill / n : fill / (n - 1);
            var res = new List<string>();
            for (int i = 1; i < n; i++)
                res.AddRange(CopyWith(a, c, Matrix3d.Rotation(step * i, Vector3d.ZAxis, a.Pt("center"))));
            return new { handles = res };
        }

        [Tool("modify.match_properties")]
        public static object Match(Args a, Ctx c)
        {
            var src = (Entity)c.Open(a.NeedStr("source"));
            int n = 0;
            foreach (var e in Ents(a, c))
            {
                e.Layer = src.Layer; e.Color = src.Color; e.Linetype = src.Linetype;
                e.LinetypeScale = src.LinetypeScale; e.LineWeight = src.LineWeight; e.Transparency = src.Transparency;
                n++;
            }
            return new { matched = n };
        }

        [Tool("modify.polyline_vertex")]
        public static object PolyVertex(Args a, Ctx c)
        {
            var pl = (Polyline)c.Open(a.NeedStr("handle"), true);
            string action = a.Str("action", "move"); // add | move | remove | bulge | width
            int i = a.Int("index");
            switch (action)
            {
                case "add": pl.AddVertexAt(i, Conv.ToPoint2d(a.Node("point")), a.Dbl("bulge"), 0, 0); break;
                case "move": pl.SetPointAt(i, Conv.ToPoint2d(a.Node("point"))); break;
                case "remove": pl.RemoveVertexAt(i); break;
                case "bulge": pl.SetBulgeAt(i, a.Dbl("bulge")); break;
                case "width": pl.SetStartWidthAt(i, a.Dbl("start_width")); pl.SetEndWidthAt(i, a.Dbl("end_width")); break;
            }
            return new { vertices = pl.NumberOfVertices };
        }
    }
}

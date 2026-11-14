using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    /// <summary>Line, polyline, rectangle, polygon, circle, arc, ellipse, spline, point, hatch, xline, ray.
    /// Every tool also accepts: layer, color, linetype, lineweight (mm), ltscale, transparency (%).</summary>
    public static class DraftingHandler
    {
        static object Out(ObjectId id) => new { handle = Ctx.H(id) };

        [Tool("drafting.line")]
        public static object DrawLine(Args a, Ctx c) => Out(c.Add(new Line(a.Pt("start"), a.Pt("end")), a));

        [Tool("drafting.polyline")]
        public static object DrawPolyline(Args a, Ctx c)
        {
            var pts = a.Pts("points");
            if (pts.Count < 2) throw new ArgumentException("Need at least 2 points.");
            var bulges = a.Dbls("bulges");
            var pl = new Polyline();
            double w = a.Dbl("width", 0);
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), i < bulges.Count ? bulges[i] : 0, w, w);
            pl.Closed = a.Bool("closed");
            if (a.Has("elevation")) pl.Elevation = a.Dbl("elevation");
            if (a.Has("global_width")) pl.ConstantWidth = a.Dbl("global_width");
            return Out(c.Add(pl, a));
        }

        [Tool("drafting.polyline3d")]
        public static object DrawPolyline3d(Args a, Ctx c)
        {
            var pl = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(a.Pts("points").ToArray()), a.Bool("closed"));
            return Out(c.Add(pl, a));
        }

        [Tool("drafting.rectangle")]
        public static object DrawRectangle(Args a, Ctx c)
        {
            var p1 = a.Pt("corner1"); var p2 = a.Pt("corner2");
            var pl = new Polyline();
            var pts = new[] { new Point2d(p1.X, p1.Y), new Point2d(p2.X, p1.Y), new Point2d(p2.X, p2.Y), new Point2d(p1.X, p2.Y) };
            for (int i = 0; i < 4; i++) pl.AddVertexAt(i, pts[i], 0, 0, 0);
            pl.Closed = true;
            if (a.Has("fillet_radius") || a.Has("chamfer")) { /* use modify tools / run_command for fillet/chamfer */ }
            var id = c.Add(pl, a);
            if (a.Has("rotation"))
                pl.TransformBy(Matrix3d.Rotation(a.Deg("rotation"), Vector3d.ZAxis, p1));
            return Out(id);
        }

        [Tool("drafting.polygon")]
        public static object DrawPolygon(Args a, Ctx c)
        {
            var ctr = a.Pt("center"); double r = a.Dbl("radius"); int n = a.Int("sides", 6);
            bool inscribed = a.Str("mode", "inscribed").StartsWith("i", StringComparison.OrdinalIgnoreCase);
            double R = inscribed ? r : r / Math.Cos(Math.PI / n);
            double start = a.Deg("rotation", inscribed ? 90 : 90 + 180.0 / n);
            var pl = new Polyline();
            for (int i = 0; i < n; i++)
            {
                double ang = start + 2 * Math.PI * i / n;
                pl.AddVertexAt(i, new Point2d(ctr.X + R * Math.Cos(ang), ctr.Y + R * Math.Sin(ang)), 0, 0, 0);
            }
            pl.Closed = true;
            return Out(c.Add(pl, a));
        }

        [Tool("drafting.circle")]
        public static object DrawCircle(Args a, Ctx c)
        {
            Circle ci;
            if (a.Has("p1") && a.Has("p2") && a.Has("p3"))
            {
                var ca = new CircularArc3d(a.Pt("p1"), a.Pt("p2"), a.Pt("p3"));
                ci = new Circle(ca.Center, Vector3d.ZAxis, ca.Radius);
            }
            else
            {
                double r = a.Has("diameter") ? a.Dbl("diameter") / 2 : a.Dbl("radius");
                ci = new Circle(a.Pt("center"), Vector3d.ZAxis, r);
            }
            return Out(c.Add(ci, a));
        }

        [Tool("drafting.arc")]
        public static object DrawArc(Args a, Ctx c)
        {
            Arc arc;
            if (a.Has("p1") && a.Has("p2") && a.Has("p3"))
            {
                var ca = new CircularArc3d(a.Pt("p1"), a.Pt("p2"), a.Pt("p3"));
                var ctr = ca.Center;
                double sa = Math.Atan2(ca.StartPoint.Y - ctr.Y, ca.StartPoint.X - ctr.X);
                double ea = Math.Atan2(ca.EndPoint.Y - ctr.Y, ca.EndPoint.X - ctr.X);
                // AutoCAD arcs go counter-clockwise; flip if the 3 points run clockwise
                var cross = (a.Pt("p2") - a.Pt("p1")).CrossProduct(a.Pt("p3") - a.Pt("p2"));
                arc = cross.Z >= 0 ? new Arc(ctr, ca.Radius, sa, ea) : new Arc(ctr, ca.Radius, ea, sa);
            }
            else
                arc = new Arc(a.Pt("center"), a.Dbl("radius"), a.Deg("start_angle"), a.Deg("end_angle"));
            return Out(c.Add(arc, a));
        }

        [Tool("drafting.ellipse")]
        public static object DrawEllipse(Args a, Ctx c)
        {
            var ctr = a.Pt("center");
            Vector3d major = a.Has("major_axis") ? Conv.ToVector3d(a.Node("major_axis"))
                : new Vector3d(a.Dbl("major_radius") * Math.Cos(a.Deg("rotation")), a.Dbl("major_radius") * Math.Sin(a.Deg("rotation")), 0);
            double ratio = a.Has("ratio") ? a.Dbl("ratio") : a.Dbl("minor_radius") / major.Length;
            var el = new Ellipse(ctr, Vector3d.ZAxis, major, ratio, a.Deg("start_angle", 0), a.Deg("end_angle", 360));
            return Out(c.Add(el, a));
        }

        [Tool("drafting.spline")]
        public static object DrawSpline(Args a, Ctx c)
        {
            Spline sp;
            if (a.Has("control_points"))
            {
                var cps = new Point3dCollection(a.Pts("control_points").ToArray());
                int degree = a.Int("degree", 3);
                int n = cps.Count;
                var knots = new DoubleCollection();
                for (int i = 0; i < n + degree + 1; i++)
                    knots.Add(i <= degree ? 0 : i >= n ? n - degree : i - degree);
                sp = new Spline(degree, false, a.Bool("closed"), false, cps, knots, new DoubleCollection(), 0, 0);
            }
            else
            {
                var pts = a.Pts("fit_points");
                if (a.Bool("closed") && pts.Count > 2) pts.Add(pts[0]);
                // 2nd argument is the ORDER (= degree + 1)
                sp = new Spline(new Point3dCollection(pts.ToArray()), a.Int("degree", 3) + 1, a.Dbl("fit_tolerance", 0));
            }
            return Out(c.Add(sp, a));
        }

        [Tool("drafting.point")]
        public static object DrawPoint(Args a, Ctx c)
        {
            var ids = new List<string>();
            foreach (var p in a.Has("points") ? a.Pts("points") : new List<Point3d> { a.Pt("location") })
                ids.Add(Ctx.H(c.Add(new DBPoint(p), a)));
            if (a.Has("pdmode")) c.Db.Pdmode = a.Int("pdmode");
            if (a.Has("pdsize")) c.Db.Pdsize = a.Dbl("pdsize");
            return new { handles = ids };
        }

        [Tool("drafting.xline")]
        public static object DrawXLine(Args a, Ctx c) =>
            Out(c.Add(new Xline { BasePoint = a.Pt("base"), UnitDir = (a.Pt("through") - a.Pt("base")).GetNormal() }, a));

        [Tool("drafting.ray")]
        public static object DrawRay(Args a, Ctx c) =>
            Out(c.Add(new Ray { BasePoint = a.Pt("base"), UnitDir = (a.Pt("through") - a.Pt("base")).GetNormal() }, a));

        [Tool("drafting.hatch")]
        public static object DrawHatch(Args a, Ctx c)
        {
            var h = new Hatch();
            var id = c.Add(h, a);
            string pattern = a.Str("pattern", "SOLID");
            h.SetHatchPattern(HatchPatternType.PreDefined, pattern);
            if (!pattern.Equals("SOLID", StringComparison.OrdinalIgnoreCase))
            {
                h.PatternScale = a.Dbl("scale", 1);
                h.PatternAngle = a.Deg("angle", 0);
                h.SetHatchPattern(HatchPatternType.PreDefined, pattern); // re-apply so scale/angle take effect
            }
            h.Associative = a.Bool("associative", true);
            var bounds = new ObjectIdCollection();
            foreach (var b in c.FindMany(a.Strs("boundaries"))) bounds.Add(b);
            h.AppendLoop(HatchLoopTypes.Outermost, bounds);
            h.EvaluateHatch(true);
            if (a.Has("background_color")) h.BackgroundColor = Conv.ToColor(a.Node("background_color"));
            return Out(id);
        }

        [Tool("drafting.donut")]
        public static object DrawDonut(Args a, Ctx c)
        {
            var ctr = a.Pt("center"); double ri = a.Dbl("inner_diameter") / 2, ro = a.Dbl("outer_diameter") / 2;
            double w = ro - ri, r = (ro + ri) / 2;
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(ctr.X - r, ctr.Y), 1, w, w);
            pl.AddVertexAt(1, new Point2d(ctr.X + r, ctr.Y), 1, w, w);
            pl.Closed = true;
            return Out(c.Add(pl, a));
        }
    }
}

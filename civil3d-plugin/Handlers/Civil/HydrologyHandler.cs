using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3DMcp.Handlers
{
    /// <summary>
    /// Small-catchment hydrology: Rational method, time of concentration, catchment area from a boundary,
    /// low point and flow path (Civil 3D water drop). SI units: m, ha, mm/h, m³/s.
    /// </summary>
    public static class HydrologyHandler
    {
        static Curve Boundary(Ctx c, string handle)
        {
            if (!(c.Open(handle) is Curve curve)) throw new ArgumentException($"'{handle}' is not a polyline or other curve.");
            if (!curve.Closed) throw new ArgumentException($"Boundary '{handle}' is not closed.");
            return curve;
        }

        static List<Point2d> SampleCurve(Curve curve, int count = 400)
        {
            var pts = new List<Point2d>();
            double end = curve.GetDistanceAtParameter(curve.EndParam);
            for (int i = 0; i < count; i++)
            {
                var p = curve.GetPointAtDist(end * i / count);
                pts.Add(new Point2d(p.X, p.Y));
            }
            return pts;
        }

        static bool Inside(List<Point2d> poly, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].Y > y) != (poly[j].Y > y) &&
                    x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            }
            return inside;
        }

        [Tool("hydrology.rational")]
        public static object Rational(Args a, Ctx c)
        {
            double f = Hydraulics.ToMeters(c.Db);
            double intensity = a.Dbl("intensity");
            if (intensity <= 0) throw new ArgumentException("intensity (rainfall intensity in mm/h) must be positive.");
            var parts = new List<(double ha, double cval, string label)>();
            if (a.Has("areas"))
            {
                foreach (var node in a.Arr("areas"))
                {
                    var item = new Args(node as System.Text.Json.Nodes.JsonObject);
                    double ha = item.Has("boundary") ? Boundary(c, item.Str("boundary")).Area * f * f / 10000.0 : item.Dbl("area_ha");
                    parts.Add((ha, item.Dbl("c"), item.Str("name", item.Str("boundary", $"area {parts.Count + 1}"))));
                }
            }
            else
            {
                double ha = a.Has("boundary") ? Boundary(c, a.Str("boundary")).Area * f * f / 10000.0 : a.Dbl("area_ha");
                parts.Add((ha, a.Dbl("c"), a.Str("boundary", "catchment")));
            }
            if (parts.Any(p => p.ha <= 0)) throw new ArgumentException("Every area needs area_ha > 0 or a closed boundary.");
            if (parts.Any(p => p.cval <= 0 || p.cval > 1)) throw new ArgumentException("Runoff coefficient c must be between 0 and 1.");
            double totalHa = parts.Sum(p => p.ha);
            double cWeighted = parts.Sum(p => p.ha * p.cval) / totalHa;
            double q = cWeighted * intensity * totalHa / 360.0;   // Q (m³/s) = C i A / 360, i in mm/h, A in ha
            return new
            {
                area_ha = Math.Round(totalHa, 4), c = Math.Round(cWeighted, 4), intensity_mm_per_h = intensity,
                peak_flow_m3s = Math.Round(q, 4), peak_flow_lps = Math.Round(q * 1000, 1),
                areas = parts.Select(p => new { name = p.label, area_ha = Math.Round(p.ha, 4), c = p.cval }).ToList(),
                formula = "Q = C i A / 360  (Q m³/s, i mm/h, A ha)",
                note = "Use the rainfall intensity for a duration equal to the time of concentration (hydrology_time_of_concentration).",
            };
        }

        [Tool("hydrology.time_of_concentration")]
        public static object TimeOfConcentration(Args a, Ctx c)
        {
            double f = Hydraulics.ToMeters(c.Db);
            double length, fall;
            if (a.Has("path"))
            {
                var (len2d, drop) = PathLengthAndFall(c, a.Str("path"));
                length = len2d * f;
                fall = drop * f;
            }
            else
            {
                length = a.Dbl("length");
                fall = a.Has("fall") ? a.Dbl("fall") : a.Dbl("slope") * length;
            }
            if (length <= 0 || fall <= 0) throw new ArgumentException("Give length (m) and fall (m) or slope (m/m), or path (a 3D polyline handle, for example from hydrology_flow_path).");
            double slope = fall / length;
            var method = a.Str("method", "all").ToLowerInvariant();
            var results = new Dictionary<string, object>();

            if (method == "all" || method == "kirpich")
                results["kirpich"] = Math.Round(0.0195 * Math.Pow(length, 0.77) * Math.Pow(slope, -0.385), 2);
            if (method == "all" || method == "california")
                results["california"] = Math.Round(60 * Math.Pow(0.87 * Math.Pow(length / 1000.0, 3) / fall, 0.385), 2);
            if (method == "all" || method == "faa")
            {
                if (a.Has("c")) results["faa"] = Math.Round(3.26 * (1.1 - a.Dbl("c")) * Math.Sqrt(length) / Math.Pow(slope * 100, 1.0 / 3.0), 2);
                else if (method == "faa") throw new ArgumentException("The FAA method needs the runoff coefficient c.");
            }
            if (results.Count == 0) throw new ArgumentException("method must be kirpich, california, faa or all.");
            return new
            {
                length_m = Math.Round(length, 2), fall_m = Math.Round(fall, 3), slope = Math.Round(slope, 5),
                tc_minutes = results,
                formulas = new
                {
                    kirpich = "tc = 0.0195 L^0.77 S^-0.385  (min; L m, S m/m)",
                    california = "tc = 60 (0.87 L³ / H)^0.385  (min; L km, H m) - California Culvert Practice, used in IRC:SP:13",
                    faa = "tc = 3.26 (1.1 - C) L^0.5 / S^(1/3)  (min; L m, S %)",
                },
            };
        }

        static (double length2d, double fall) PathLengthAndFall(Ctx c, string handle)
        {
            var o = c.Open(handle);
            var pts = new List<Point3d>();
            if (o is Polyline3d p3)
                foreach (ObjectId vid in p3) pts.Add(((PolylineVertex3d)c.Open(vid)).Position);
            else if (o is Polyline lw)
                for (int i = 0; i < lw.NumberOfVertices; i++) pts.Add(lw.GetPoint3dAt(i));
            else if (o is Curve cv)
            {
                pts.Add(cv.StartPoint);
                pts.Add(cv.EndPoint);
            }
            if (pts.Count < 2) throw new ArgumentException($"'{handle}' has no usable path.");
            double len = 0;
            for (int i = 1; i < pts.Count; i++) len += new Point2d(pts[i - 1].X, pts[i - 1].Y).GetDistanceTo(new Point2d(pts[i].X, pts[i].Y));
            return (len, pts[0].Z - pts[pts.Count - 1].Z);
        }

        [Tool("hydrology.catchment_area")]
        public static object CatchmentArea(Args a, Ctx c)
        {
            double f = Hydraulics.ToMeters(c.Db);
            var curve = Boundary(c, a.NeedStr("boundary"));
            double m2 = curve.Area * f * f;
            double perimeter = curve.GetDistanceAtParameter(curve.EndParam) * f;
            object elevations = null;
            if (a.Has("surface"))
            {
                var s = (CivilSurface)c.Open(a.Str("surface"));
                var stats = SampleInside(s, SampleCurve(curve), a.Int("grid", 60));
                if (stats != null)
                {
                    var (lo, hi, mean, count) = stats.Value;
                    elevations = new
                    {
                        min = Math.Round(lo.Z * f, 3), max = Math.Round(hi.Z * f, 3), mean = Math.Round(mean * f, 3),
                        relief = Math.Round((hi.Z - lo.Z) * f, 3), lowest_point = new[] { lo.X, lo.Y, lo.Z }, samples = count,
                    };
                }
            }
            return new { area_m2 = Math.Round(m2, 2), area_ha = Math.Round(m2 / 10000, 4), area_km2 = Math.Round(m2 / 1e6, 6),
                         perimeter_m = Math.Round(perimeter, 2), elevations };
        }

        static (Point3d low, Point3d high, double mean, int count)? SampleInside(CivilSurface s, List<Point2d> poly, int grid)
        {
            grid = Math.Max(10, Math.Min(grid, 300));
            double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X), minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
            Point3d? lo = null, hi = null;
            double sum = 0; int count = 0;
            for (int i = 0; i <= grid; i++)
                for (int j = 0; j <= grid; j++)
                {
                    double x = minX + (maxX - minX) * i / grid, y = minY + (maxY - minY) * j / grid;
                    if (poly.Count > 2 && !Inside(poly, x, y)) continue;
                    double z;
                    try { z = s.FindElevationAtXY(x, y); } catch { continue; }
                    var p = new Point3d(x, y, z);
                    if (lo == null || z < lo.Value.Z) lo = p;
                    if (hi == null || z > hi.Value.Z) hi = p;
                    sum += z; count++;
                }
            if (count == 0) return null;
            return (lo.Value, hi.Value, sum / count, count);
        }

        [Tool("hydrology.low_point")]
        public static object LowPoint(Args a, Ctx c)
        {
            var s = (CivilSurface)c.Open(a.NeedStr("surface"));
            List<Point2d> poly;
            if (a.Has("boundary")) poly = SampleCurve(Boundary(c, a.Str("boundary")));
            else
            {
                var g = s.GetGeneralProperties();
                poly = new List<Point2d> { new Point2d(g.MinimumCoordinateX, g.MinimumCoordinateY), new Point2d(g.MaximumCoordinateX, g.MaximumCoordinateY) };
            }
            var stats = SampleInside(s, poly, a.Int("grid", 80));
            if (stats == null) throw new InvalidOperationException("No surface data inside the area.");
            var (lo, hi, mean, count) = stats.Value;
            return new
            {
                low_point = new[] { Math.Round(lo.X, 3), Math.Round(lo.Y, 3), Math.Round(lo.Z, 3) },
                high_point = new[] { Math.Round(hi.X, 3), Math.Round(hi.Y, 3), Math.Round(hi.Z, 3) },
                mean_elevation = Math.Round(mean, 3), samples = count,
                note = "Found by sampling the surface on a grid; increase grid for more precision.",
            };
        }

        [Tool("hydrology.flow_path")]
        public static object FlowPath(Args a, Ctx c)
        {
            var s = (CivilSurface)c.Open(a.NeedStr("surface"), true);
            var start = a.Pt("start");
            var created = s.Analysis.CreateWaterdrop(new Point2d(start.X, start.Y), Autodesk.Civil.WaterdropObjectType.Polyline3D);
            var ids = Ctx.SafeIds(created).ToList();
            if (ids.Count == 0) throw new InvalidOperationException("Civil 3D did not create a flow path here (the start may be outside the surface or on a flat area).");
            double f = Hydraulics.ToMeters(c.Db);
            var paths = new List<object>();
            foreach (var id in ids)
            {
                if (a.Has("layer")) { c.EnsureLayer(a.Str("layer")); ((Entity)c.Open(id, true)).Layer = a.Str("layer"); }
                var (len, fall) = PathLengthAndFall(c, Ctx.H(id));
                var pl = c.Open(id) as Curve;
                paths.Add(new
                {
                    handle = Ctx.H(id), length_m = Math.Round(len * f, 2), fall_m = Math.Round(fall * f, 3),
                    end_point = pl == null ? null : new[] { Math.Round(pl.EndPoint.X, 3), Math.Round(pl.EndPoint.Y, 3), Math.Round(pl.EndPoint.Z, 3) },
                });
            }
            return new { paths, note = "Pass a path handle to hydrology_time_of_concentration to get tc from its length and fall." };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3DMcp.Handlers
{
    /// <summary>
    /// Pressure (water supply) networks: parts lists, network, pipes, fittings, appurtenances and a cover check.
    /// Civil 3D's API has no network-level cover setter, so cover is applied when a pipe is placed and checked
    /// afterwards against a surface.
    /// </summary>
    public static class PressureHandler
    {
        const string CatalogHint = " If the parts list is empty, set the pressure catalog for the drawing first: "
            + "Home tab > Create Design > Pressure Network (drop-down) > Set Pressure Network Catalog "
            + "(command AECCSETPRESSURENETWORKCATALOG).";

        static readonly PressurePartType[] PipeTypes = { PressurePartType.PressurePipe };
        static readonly PressurePartType[] FittingTypes =
        {
            PressurePartType.Elbow, PressurePartType.Tee, PressurePartType.Wye, PressurePartType.Cross,
            PressurePartType.Cap, PressurePartType.Coupling, PressurePartType.Plug, PressurePartType.Reducer,
        };
        static readonly PressurePartType[] AppurtenanceTypes =
        {
            PressurePartType.Valve, PressurePartType.Pump, PressurePartType.Hydrant,
        };

        static IEnumerable<ObjectId> PartsListIds(Ctx c) => Ctx.SafeIds(c.Civ.Styles.GetPressurePartLists());

        static PressurePartList PartsList(Ctx c, ObjectId id) => (PressurePartList)c.Open(id);

        static List<string> PartNames(PressurePartList list, PressurePartType type)
        {
            try { return list.GetParts(type).Where(p => p.IsValid).Select(p => p.Description).ToList(); }
            catch { return new List<string>(); }
        }

        static PressurePartSize FindPart(Ctx c, PressurePipeNetwork net, string part, PressurePartType[] types)
        {
            if (net.PartsListId.IsNull) throw new InvalidOperationException("The pressure network has no parts list." + CatalogHint);
            var list = PartsList(c, net.PartsListId);
            var available = new List<string>();
            foreach (var t in types)
            {
                IEnumerable<PressurePartSize> parts;
                try { parts = list.GetParts(t); } catch { continue; }
                foreach (var p in parts)
                {
                    if (!p.IsValid) continue;
                    if (string.Equals(p.Description, part, StringComparison.OrdinalIgnoreCase)) return p;
                    available.Add(p.Description);
                }
            }
            var hint = available.Count == 0 ? CatalogHint : " Available: " + string.Join(" | ", available.Take(40));
            throw new ArgumentException($"Part '{part}' was not found in parts list '{list.Name}'.{hint}");
        }

        static double PartOuterDiameter(object part)
        {
            foreach (var p in new[] { "OuterDiameter", "NominalDiameter", "InnerDiameter" })
            {
                try { var v = Convert.ToDouble(Reflect.Get(part, p)); if (v > 0) return v; } catch { }
            }
            return 0;
        }

        static double PipeOuterDiameter(object pipe)
        {
            foreach (var p in new[] { "OuterDiameter", "OuterDiameterOrWidth", "InnerDiameter", "InnerDiameterOrWidth" })
            {
                try { var v = Convert.ToDouble(Reflect.Get(pipe, p)); if (v > 0) return v; } catch { }
            }
            return 0;
        }

        // Civil 3D only says "Fail to add ..." when it refuses a pressure part. Explain what we know.
        static Exception Refused(string what, Exception ex) => new InvalidOperationException(
            $"Civil 3D refused to add the {what} ({(ex.InnerException ?? ex).Message}). This is decided inside Civil 3D: check that you can "
            + "draw the same part with Civil 3D's own Pressure Network Creation Tools. If that fails too, update Civil 3D (Autodesk Access) or "
            + "try another pressure catalog (AECCSETPRESSURENETWORKCATALOG, e.g. Metric_AWWA_PushOn). On Civil 3D 2025.0.1 with the stock "
            + "metric catalogs this failed in the Civil 3D interface as well. pressure_check_cover and the quantity tools still work on "
            + "pressure networks drawn in Civil 3D.", ex);

        static double? SurfaceZ(CivilSurface s, double x, double y)
        {
            try { return s.FindElevationAtXY(x, y); } catch { return null; }
        }

        [Tool("pressure.parts_lists", ReadOnly = true)]
        public static object PartsLists(Args a, Ctx c)
        {
            var res = new List<object>();
            foreach (var id in PartsListIds(c))
            {
                var list = PartsList(c, id);
                var byType = new Dictionary<string, List<string>>();
                foreach (PressurePartType t in Enum.GetValues(typeof(PressurePartType)))
                {
                    var names = PartNames(list, t);
                    if (names.Count > 0) byType[t.ToString()] = names;
                }
                res.Add(new { name = list.Name, parts = byType });
            }
            if (res.Count == 0) return new { parts_lists = res, note = "No pressure parts lists in this drawing." + CatalogHint };
            return res;
        }

        [Tool("pressure.network_create")]
        public static object NetworkCreate(Args a, Ctx c)
        {
            var listIds = PartsListIds(c).ToList();
            if (listIds.Count == 0) throw new InvalidOperationException("No pressure parts lists in this drawing." + CatalogHint);
            ObjectId listId = listIds[0];
            if (a.Has("parts_list"))
            {
                listId = listIds.FirstOrDefault(id => string.Equals(PartsList(c, id).Name, a.Str("parts_list"), StringComparison.OrdinalIgnoreCase));
                if (listId.IsNull)
                    throw new ArgumentException($"Pressure parts list '{a.Str("parts_list")}' not found. Available: "
                        + string.Join(" | ", listIds.Select(id => PartsList(c, id).Name)));
            }
            var id = PressurePipeNetwork.Create(c.Db, a.Str("name", "Pressure Network"));
            var net = (PressurePipeNetwork)c.Open(id, true);
            net.PartsListId = listId;
            if (a.Has("surface")) net.ReferenceSurfaceId = c.Find(a.Str("surface"));
            if (a.Has("alignment")) net.ReferenceAlignmentId = c.Find(a.Str("alignment"));
            return new { handle = Ctx.H(id), name = net.Name, parts_list = PartsList(c, listId).Name };
        }

        [Tool("pressure.add_pipe")]
        public static object AddPipe(Args a, Ctx c)
        {
            var net = (PressurePipeNetwork)c.Open(a.NeedStr("network"), true);
            var part = FindPart(c, net, a.NeedStr("part"), PipeTypes);
            var start = a.Pt("start");
            var end = a.Pt("end");
            var warnings = new List<string>();

            if (a.Has("cover"))
            {
                // Place the pipe so its top sits `cover` below the surface at both ends.
                var surfaceId = a.Has("surface") ? c.Find(a.Str("surface")) : net.ReferenceSurfaceId;
                if (surfaceId.IsNull) throw new ArgumentException("cover needs a surface: pass surface or create the network with a reference surface.");
                var s = (CivilSurface)c.Open(surfaceId);
                double cover = a.Dbl("cover"), half = PartOuterDiameter(part) / 2;
                if (half == 0) warnings.Add("The part's diameter could not be read, so cover is measured to the pipe centreline.");
                var z0 = SurfaceZ(s, start.X, start.Y);
                var z1 = SurfaceZ(s, end.X, end.Y);
                if (z0 == null || z1 == null) throw new ArgumentException("The pipe start or end is outside the surface.");
                start = new Point3d(start.X, start.Y, z0.Value - cover - half);
                end = new Point3d(end.X, end.Y, z1.Value - cover - half);
            }

            ObjectId id;
            try { id = net.AddLinePipe(new LineSegment3d(start, end), part); }
            catch (Exception ex) { throw Refused("pipe", ex); }
            var pipe = c.Open(id, true);
            if (a.Has("name")) try { Reflect.Set(pipe, "Name", a.Str("name")); } catch (Exception ex) { warnings.Add("name was not set: " + ex.Message); }
            var res = Props.Pick(pipe, "Name", "PartDescription", "Length2D", "Length3D", "StartPoint", "EndPoint", "InnerDiameter", "OuterDiameter");
            res["handle"] = Ctx.H(id);
            if (warnings.Count > 0) res["warnings"] = warnings;
            return res;
        }

        static object AddComponent(Args a, Ctx c, bool fitting)
        {
            var net = (PressurePipeNetwork)c.Open(a.NeedStr("network"), true);
            var part = FindPart(c, net, a.NeedStr("part"), fitting ? FittingTypes : AppurtenanceTypes);
            var loc = a.Pt("location");
            ObjectId id;
            try { id = fitting ? net.AddFitting(loc, part) : net.AddAppurtenance(loc, part); }
            catch (Exception ex) { throw Refused(fitting ? "fitting" : "appurtenance", ex); }
            var o = c.Open(id, true);
            var warnings = new List<string>();
            if (a.Has("name")) try { Reflect.Set(o, "Name", a.Str("name")); } catch (Exception ex) { warnings.Add("name was not set: " + ex.Message); }
            var res = Props.Pick(o, "Name", "PartDescription", "Position", "Location");
            res["handle"] = Ctx.H(id);
            if (warnings.Count > 0) res["warnings"] = warnings;
            return res;
        }

        [Tool("pressure.add_fitting")]
        public static object AddFitting(Args a, Ctx c) => AddComponent(a, c, true);

        [Tool("pressure.add_appurtenance")]
        public static object AddAppurtenance(Args a, Ctx c) => AddComponent(a, c, false);

        [Tool("pressure.check_cover", ReadOnly = true)]
        public static object CheckCover(Args a, Ctx c)
        {
            var net = (PressurePipeNetwork)c.Open(a.NeedStr("network"));
            var surfaceId = a.Has("surface") ? c.Find(a.Str("surface")) : net.ReferenceSurfaceId;
            if (surfaceId.IsNull) throw new ArgumentException("Pass surface, or create the network with a reference surface.");
            var s = (CivilSurface)c.Open(surfaceId);
            double minCover = a.Dbl("min_cover", 1.0);
            double? maxCover = a.Has("max_cover") ? a.Dbl("max_cover") : (double?)null;

            var pipes = new List<object>();
            int problems = 0;
            foreach (var id in Ctx.SafeIds(net.GetPipeIds()))
            {
                var pipe = c.Open(id);
                var sp = (Point3d)Reflect.Get(pipe, "StartPoint");
                var ep = (Point3d)Reflect.Get(pipe, "EndPoint");
                double half = PipeOuterDiameter(pipe) / 2;
                var samples = new[] { ("start", sp), ("middle", new Point3d((sp.X + ep.X) / 2, (sp.Y + ep.Y) / 2, (sp.Z + ep.Z) / 2)), ("end", ep) };
                var covers = new Dictionary<string, object>();
                var issues = new List<string>();
                foreach (var (label, p) in samples)
                {
                    var z = SurfaceZ(s, p.X, p.Y);
                    if (z == null) { covers[label] = null; issues.Add($"{label} is outside the surface"); continue; }
                    double cov = Math.Round(z.Value - (p.Z + half), 3);
                    covers[label] = cov;
                    if (cov < minCover) issues.Add($"{label} cover {cov} < {minCover}");
                    if (maxCover.HasValue && cov > maxCover.Value) issues.Add($"{label} cover {cov} > {maxCover}");
                }
                if (issues.Count > 0) problems++;
                pipes.Add(new { name = Reflect.Get(pipe, "Name"), handle = Ctx.H(id), cover = covers, ok = issues.Count == 0, issues });
            }
            return new { network = net.Name, surface = Reflect.Get(s, "Name"), min_cover = minCover, max_cover = maxCover,
                         pipes_checked = pipes.Count, pipes_with_problems = problems, pipes,
                         note = "Cover is measured from the surface to the top of the pipe (outer diameter when available)." };
        }
    }
}

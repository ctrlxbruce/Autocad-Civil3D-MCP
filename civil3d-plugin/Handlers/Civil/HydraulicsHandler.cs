using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Civil3DMcp.Handlers
{
    /// <summary>
    /// Gravity network design checks: Manning capacity / velocity / depth ratio, a simplified HGL,
    /// and geometric QC (slope, cover, sumps, connections). Results are SI (m, m/s, L/s).
    /// </summary>
    public static class HydraulicsHandler
    {
        sealed class PipeInfo
        {
            public string Name, Handle, Size;
            public Pipe Pipe;
            public double D, OD, Length, InvStart, InvEnd, Slope;
            public ObjectId StartStructure, EndStructure;
            public Point3d StartPt, EndPt;
            // Flow runs from the higher invert to the lower one.
            public bool FlowsStartToEnd => InvStart >= InvEnd;
            public ObjectId Upstream => FlowsStartToEnd ? StartStructure : EndStructure;
            public ObjectId Downstream => FlowsStartToEnd ? EndStructure : StartStructure;
            public double InvUp => Math.Max(InvStart, InvEnd);
            public double InvDown => Math.Min(InvStart, InvEnd);
        }

        static List<PipeInfo> ReadPipes(Ctx c, Network net, double f)
        {
            var list = new List<PipeInfo>();
            foreach (ObjectId id in net.GetPipeIds())
            {
                var p = (Pipe)c.Open(id);
                double d = p.InnerDiameterOrWidth * f;
                double od = CivilCommonHandler.Safe(() => p.OuterDiameterOrWidth) * f;
                double half = p.InnerDiameterOrWidth / 2;
                var info = new PipeInfo
                {
                    Name = p.Name, Handle = Ctx.H(id), Pipe = p, D = d, OD = od > 0 ? od : d,
                    Size = CivilCommonHandler.Safe(() => p.PartSizeName),
                    Length = p.Length2D * f,
                    InvStart = (p.StartPoint.Z - half) * f, InvEnd = (p.EndPoint.Z - half) * f,
                    StartStructure = p.StartStructureId, EndStructure = p.EndStructureId,
                    StartPt = p.StartPoint, EndPt = p.EndPoint,
                };
                info.Slope = info.Length > 0 ? Math.Abs(info.InvStart - info.InvEnd) / info.Length : 0;
                list.Add(info);
            }
            return list;
        }

        static Dictionary<string, double> ReadFlows(Args a)
        {
            var flows = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var o = a.Obj("flows");
            if (o != null) foreach (var kv in o) flows[kv.Key] = Conv.NodeToDouble(kv.Value);
            return flows;
        }

        [Tool("pipes.hydraulics", ReadOnly = true)]
        public static object PipeHydraulics(Args a, Ctx c)
        {
            var net = (Network)c.Open(a.NeedStr("network"));
            double f = Hydraulics.ToMeters(c.Db);
            double n = a.Dbl("n", 0.013);
            double vMin = a.Dbl("min_velocity", 0.6), vMax = a.Dbl("max_velocity", 3.0), dMax = a.Dbl("max_depth_ratio", 0.8);
            var flows = ReadFlows(a);   // L/s per pipe name
            var pipes = ReadPipes(c, net, f);
            var unknownFlowNames = flows.Keys.Where(k => pipes.All(p => !string.Equals(p.Name, k, StringComparison.OrdinalIgnoreCase))).ToList();

            var rows = new List<Dictionary<string, object>>();
            var normal = new Dictionary<string, (double depth, double velocity)?>();
            foreach (var p in pipes)
            {
                var (qFull, vFull) = Hydraulics.FullFlow(p.D, p.Slope, n);
                var row = new Dictionary<string, object>
                {
                    ["pipe"] = p.Name, ["size"] = p.Size, ["diameter_m"] = Math.Round(p.D, 4), ["length_m"] = Math.Round(p.Length, 3),
                    ["slope_percent"] = Math.Round(p.Slope * 100, 4), ["full_capacity_lps"] = Math.Round(qFull * 1000, 2),
                    ["full_velocity_mps"] = Math.Round(vFull, 3),
                };
                var issues = new List<string>();
                if (p.Slope <= 0) issues.Add("zero slope: no gravity flow");
                if (flows.TryGetValue(p.Name, out var lps))
                {
                    double q = lps / 1000.0;
                    var nd = Hydraulics.NormalDepth(p.D, p.Slope, n, q);
                    normal[p.Name] = nd;
                    row["design_flow_lps"] = lps;
                    row["percent_of_capacity"] = qFull > 0 ? Math.Round(100 * q / qFull, 1) : (object)null;
                    if (nd == null) { row["depth_ratio"] = null; issues.Add("surcharged: design flow is more than the pipe carries part-full"); }
                    else
                    {
                        row["depth_ratio"] = Math.Round(nd.Value.depth / p.D, 3);
                        row["velocity_mps"] = Math.Round(nd.Value.velocity, 3);
                        if (nd.Value.depth / p.D > dMax) issues.Add($"depth ratio {Math.Round(nd.Value.depth / p.D, 2)} > {dMax}");
                        if (nd.Value.velocity < vMin) issues.Add($"velocity {Math.Round(nd.Value.velocity, 2)} m/s < {vMin} (not self-cleansing)");
                        if (nd.Value.velocity > vMax) issues.Add($"velocity {Math.Round(nd.Value.velocity, 2)} m/s > {vMax}");
                    }
                }
                row["ok"] = issues.Count == 0;
                row["issues"] = issues;
                rows.Add(row);
            }

            object hgl = null;
            if (flows.Count > 0) hgl = SimplifiedHgl(c, pipes, flows, normal, n, a.Has("outlet_hgl") ? a.Dbl("outlet_hgl") : (double?)null, rows);

            return new
            {
                network = net.Name, mannings_n = n, criteria = new { min_velocity = vMin, max_velocity = vMax, max_depth_ratio = dMax },
                pipes = rows, hgl,
                unknown_flow_pipes = unknownFlowNames.Count > 0 ? unknownFlowNames : null,
                note = "Manning's equation for circular pipes; slope from the pipe inverts; flows in L/s. "
                     + "Criteria defaults follow common sewer practice (CPHEEO: 0.6 m/s self-cleansing, d/D 0.8); change them to your standard.",
            };
        }

        // Simplified HGL: from the outlet(s) upstream, HGL at the upstream end = max(normal-depth level,
        // downstream HGL + full-pipe friction loss). No structure losses.
        static object SimplifiedHgl(Ctx c, List<PipeInfo> pipes, Dictionary<string, double> flows,
                                    Dictionary<string, (double depth, double velocity)?> normal, double n, double? outletHgl,
                                    List<Dictionary<string, object>> rows)
        {
            double f = Hydraulics.ToMeters(c.Db);
            var structHgl = new Dictionary<ObjectId, double>();
            var done = new HashSet<PipeInfo>();
            var pipeHgl = new Dictionary<string, object>();
            var withFlow = pipes.Where(p => flows.ContainsKey(p.Name)).ToList();

            for (int pass = 0; pass < withFlow.Count + 1; pass++)
            {
                bool progress = false;
                foreach (var p in withFlow)
                {
                    if (done.Contains(p)) continue;
                    bool isOutletPipe = p.Downstream.IsNull || !withFlow.Any(o => o != p && o.Upstream == p.Downstream);
                    double? down = null;
                    if (!p.Downstream.IsNull && structHgl.TryGetValue(p.Downstream, out var sh)) down = sh;
                    else if (isOutletPipe) down = outletHgl;
                    else continue;   // wait until the downstream pipe is solved

                    var nd = normal.TryGetValue(p.Name, out var v) ? v : null;
                    double q = flows[p.Name] / 1000.0;
                    double normalDown = p.InvDown + (nd?.depth ?? p.D);
                    double normalUp = p.InvUp + (nd?.depth ?? p.D);
                    double hDown = Math.Max(down ?? normalDown, normalDown);
                    double hUp = Math.Max(normalUp, hDown + Hydraulics.FrictionSlopeFull(p.D, q, n) * p.Length);
                    if (!p.Upstream.IsNull) structHgl[p.Upstream] = structHgl.TryGetValue(p.Upstream, out var e) ? Math.Max(e, hUp) : hUp;
                    bool surcharged = hUp > p.InvUp + p.D + 1e-6 || hDown > p.InvDown + p.D + 1e-6;
                    pipeHgl[p.Name] = new { hgl_upstream = Math.Round(hUp, 3), hgl_downstream = Math.Round(hDown, 3), surcharged };
                    var row = rows.First(r => (string)r["pipe"] == p.Name);
                    row["hgl_upstream"] = Math.Round(hUp, 3);
                    row["hgl_downstream"] = Math.Round(hDown, 3);
                    if (surcharged) { ((List<string>)row["issues"]).Add("HGL above the pipe crown (surcharged)"); row["ok"] = false; }
                    done.Add(p);
                    progress = true;
                }
                if (!progress) break;
            }

            var structures = new List<object>();
            foreach (var kv in structHgl)
            {
                var s = (Structure)c.Open(kv.Key);
                double rim = s.RimElevation * f;
                structures.Add(new { structure = s.Name, rim = Math.Round(rim, 3), hgl = Math.Round(kv.Value, 3),
                                     freeboard = Math.Round(rim - kv.Value, 3), flooding = kv.Value > rim });
            }
            var unsolved = withFlow.Where(p => !done.Contains(p)).Select(p => p.Name).ToList();
            return new { structures, unsolved_pipes = unsolved.Count > 0 ? unsolved : null,
                         method = "Simplified HGL (friction losses only), solved from the outlet upstream." };
        }

        [Tool("pipes.qc", ReadOnly = true)]
        public static object PipeQc(Args a, Ctx c)
        {
            var net = (Network)c.Open(a.NeedStr("network"));
            double f = Hydraulics.ToMeters(c.Db);
            var surfaceId = a.Has("surface") ? c.Find(a.Str("surface")) : net.ReferenceSurfaceId;
            var surface = surfaceId.IsNull ? null : (CivilSurface)c.Open(surfaceId);
            double minCover = a.Dbl("min_cover", 1.0);
            double? maxCover = a.Has("max_cover") ? a.Dbl("max_cover") : (double?)null;
            double? minSlope = a.Has("min_slope") ? a.Dbl("min_slope") : (double?)null;
            double? maxSlope = a.Has("max_slope") ? a.Dbl("max_slope") : (double?)null;

            double? Ground(Point3d p) { try { return surface?.FindElevationAtXY(p.X, p.Y) * f; } catch { return null; } }

            var pipes = ReadPipes(c, net, f);
            var pipeRows = new List<object>();
            int pipeProblems = 0;
            foreach (var p in pipes)
            {
                var issues = new List<string>();
                if (p.StartStructure.IsNull) issues.Add("start is not connected to a structure");
                if (p.EndStructure.IsNull) issues.Add("end is not connected to a structure");
                if (p.Slope <= 0) issues.Add("zero slope");
                if (minSlope.HasValue && p.Slope < minSlope.Value) issues.Add($"slope {Math.Round(p.Slope, 5)} < {minSlope}");
                if (maxSlope.HasValue && p.Slope > maxSlope.Value) issues.Add($"slope {Math.Round(p.Slope, 5)} > {maxSlope}");
                object cover = null;
                if (surface != null)
                {
                    var gs = Ground(p.StartPt);
                    var ge = Ground(p.EndPt);
                    double? cs = gs.HasValue ? Math.Round(gs.Value - (p.InvStart + p.OD), 3) : (double?)null;
                    double? ce = ge.HasValue ? Math.Round(ge.Value - (p.InvEnd + p.OD), 3) : (double?)null;
                    cover = new { start = cs, end = ce };
                    foreach (var (label, cv) in new[] { ("start", cs), ("end", ce) })
                    {
                        if (cv == null) { issues.Add($"{label} is outside the surface"); continue; }
                        if (cv < minCover) issues.Add($"{label} cover {cv} < {minCover}");
                        if (maxCover.HasValue && cv > maxCover) issues.Add($"{label} cover {cv} > {maxCover}");
                    }
                }
                if (issues.Count > 0) pipeProblems++;
                pipeRows.Add(new { pipe = p.Name, size = p.Size, slope_percent = Math.Round(p.Slope * 100, 4),
                                   invert_start = Math.Round(p.InvStart, 3), invert_end = Math.Round(p.InvEnd, 3), cover, ok = issues.Count == 0, issues });
            }

            var structRows = new List<object>();
            int structProblems = 0;
            foreach (ObjectId sid in net.GetStructureIds())
            {
                var s = (Structure)c.Open(sid);
                var issues = new List<string>();
                var connected = pipes.Where(p => p.StartStructure == sid || p.EndStructure == sid).ToList();
                double rim = s.RimElevation * f, sump = s.SumpElevation * f;
                if (connected.Count == 0) issues.Add("no pipes connected");
                else
                {
                    double lowest = connected.Min(p => p.StartStructure == sid ? p.InvStart : p.InvEnd);
                    if (sump > lowest + 0.001) issues.Add($"sump {Math.Round(sump, 3)} is above the lowest pipe invert {Math.Round(lowest, 3)}");
                }
                if (surface != null)
                {
                    var g = Ground(s.Position);
                    if (g == null) issues.Add("outside the surface");
                    else if (Math.Abs(g.Value - rim) > a.Dbl("rim_tolerance", 0.05)) issues.Add($"rim {Math.Round(rim, 3)} differs from ground {Math.Round(g.Value, 3)}");
                }
                if (issues.Count > 0) structProblems++;
                structRows.Add(new { structure = s.Name, rim = Math.Round(rim, 3), sump = Math.Round(sump, 3), pipes = connected.Count, ok = issues.Count == 0, issues });
            }

            return new
            {
                network = net.Name, surface = surface == null ? null : Reflect.Get(surface, "Name"),
                criteria = new { min_cover = minCover, max_cover = maxCover, min_slope = minSlope, max_slope = maxSlope },
                summary = new { pipes = pipeRows.Count, pipes_with_problems = pipeProblems, structures = structRows.Count, structures_with_problems = structProblems },
                pipes = pipeRows, structures = structRows,
                note = surface == null ? "No surface: cover and rim checks were skipped. Pass surface or set the network's reference surface." : null,
            };
        }
    }
}

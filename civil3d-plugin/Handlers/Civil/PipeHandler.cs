using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    /// <summary>Gravity pipe networks: parts lists, pipes, structures, connections, rules, info.
    /// Pressure networks: list and inspect (create via generic tools – see README).</summary>
    public static class PipeHandler
    {
        const string CatalogHint = " — If Civil 3D's command line says it cannot find the catalog registry information, the pipe "
            + "network catalog is not set for this drawing: in Civil 3D go to Home tab > Create Design > Pipe Network (drop-down) > "
            + "Set Pipe Network Catalog (command AECCSETNETWORKCATALOG), pick the Metric or Imperial catalog, then try again.";

        static IEnumerable<ObjectId> Families(object partsList) =>
            new[] { "Pipe", "Structure" }.SelectMany(d =>
            {
                try { return Ctx.SafeIds(Reflect.Call(partsList, "GetPartFamilyIdsByDomain", d)); }
                catch { return Enumerable.Empty<ObjectId>(); }
            });

        static string PartName(Ctx c, ObjectId id)
        {
            var o = c.Open(id);
            foreach (var p in new[] { "Description", "Name", "SizeName" })
                try { if (Reflect.Get(o, p) is string s && s.Length > 0) return s; } catch { }
            return Ctx.H(id);
        }

        [Tool("pipes.parts_lists")]
        public static object PartsLists(Args a, Ctx c)
        {
            var res = new List<object>();
            foreach (var plId in Ctx.SafeIds(Reflect.Get(c.Civ.Styles, "PartsListSet")))
            {
                var pl = c.Open(plId);
                var fams = new List<object>();
                foreach (var fid in Families(pl))
                {
                    var f = c.Open(fid);
                    int sc = Convert.ToInt32(CivilCommonHandler.Safe(() => Reflect.Get(f, "PartSizeCount")));
                    var sizes = new List<string>();
                    for (int k = 0; k < sc; k++) sizes.Add(CivilCommonHandler.Safe(() => PartName(c, (ObjectId)Reflect.Index(f, k))));
                    fams.Add(new { name = PartName(c, fid), domain = Conv.Plain(CivilCommonHandler.Safe(() => Reflect.Get(f, "Domain"))), sizes });
                }
                res.Add(new { name = Reflect.Get(pl, "Name"), families = fams });
            }
            return res;
        }

        [Tool("pipes.network_create")]
        public static object NetworkCreate(Args a, Ctx c)
        {
            var name = a.Str("name", "Pipe Network");
            var id = (ObjectId)Reflect.CallStaticOut(Reflect.T("Network"), "Create", out var outArgs, c.Civ, name);
            var net = c.Open(id, true);
            Reflect.Set(net, "PartsListId", c.StyleId("PartsListSet", a.Str("parts_list")));
            if (a.Has("surface")) Reflect.Set(net, "ReferenceSurfaceId", c.Find(a.Str("surface")));
            if (a.Has("alignment")) Reflect.Set(net, "ReferenceAlignmentId", c.Find(a.Str("alignment")));
            if (a.Has("layer")) try { Reflect.Set(net, "LayerName", a.Str("layer")); } catch { }
            return new { handle = Ctx.H(id), name = outArgs[1] };
        }

        static (ObjectId fam, ObjectId size) Part(Ctx c, object net, string family, object size)
        {
            var pl = c.Open((ObjectId)Reflect.Get(net, "PartsListId"));
            var famId = Families(pl).FirstOrDefault(id => string.Equals(PartName(c, id), family, StringComparison.OrdinalIgnoreCase));
            if (famId.IsNull)
                throw new ArgumentException($"Part family '{family}' not in parts list. Available: {string.Join(" | ", Families(pl).Select(id => PartName(c, id)))}");
            var fam = c.Open(famId);
            int sc = Convert.ToInt32(Reflect.Get(fam, "PartSizeCount"));
            ObjectId sizeId = ObjectId.Null;
            if (size is double d && d >= 0 && d < sc) sizeId = (ObjectId)Reflect.Index(fam, (int)d);
            else
                for (int k = 0; k < sc; k++)
                {
                    var sid = (ObjectId)Reflect.Index(fam, k);
                    if (string.Equals(PartName(c, sid), size?.ToString(), StringComparison.OrdinalIgnoreCase)) { sizeId = sid; break; }
                }
            if (sizeId.IsNull) sizeId = (ObjectId)Reflect.Index(fam, 0);
            return (famId, sizeId);
        }

        [Tool("pipes.add_structure")]
        public static object AddStructure(Args a, Ctx c)
        {
            var net = c.Open(a.NeedStr("network"), true);
            var (fam, size) = Part(c, net, a.NeedStr("family"), Conv.Unwrap(a.Node("size")) ?? 0.0);
            object[] r;
            try { Reflect.CallOut(net, "AddStructure", out r, fam, size, a.Pt("location"), a.Deg("rotation"), ObjectId.Null, a.Bool("apply_rules", true)); }
            catch (Exception ex) { throw new InvalidOperationException(ex.Message + CatalogHint); }
            var sid = (ObjectId)r[4];
            var st = c.Open(sid, true);
            var warnings = new List<string>();
            if (a.Has("rim")) Try(warnings, "rim", () => Reflect.Set(st, "RimElevation", a.Dbl("rim")));
            if (a.Has("sump")) Try(warnings, "sump", () => Reflect.Set(st, "SumpDepth", a.Dbl("sump")));
            if (a.Has("name")) Try(warnings, "name", () => Reflect.Set(st, "Name", a.Str("name")));
            var res = Props.Pick(st, "Name", "RimElevation", "SumpElevation", "SumpDepth");
            res["handle"] = Ctx.H(sid);
            if (warnings.Count > 0) res["warnings"] = warnings;
            return res;
        }

        // Runs one optional setting; a failure becomes a warning in the reply instead of being hidden.
        static void Try(List<string> warnings, string what, Action act)
        {
            try { act(); }
            catch (Exception ex) { warnings.Add($"{what} was not set: {(ex.InnerException ?? ex).Message}"); }
        }

        [Tool("pipes.add_pipe")]
        public static object AddPipe(Args a, Ctx c)
        {
            var net = c.Open(a.NeedStr("network"), true);
            var (fam, size) = Part(c, net, a.NeedStr("family"), Conv.Unwrap(a.Node("size")) ?? 0.0);
            var seg = new LineSegment3d(a.Pt("start"), a.Pt("end"));
            object[] r;
            try { Reflect.CallOut(net, "AddLinePipe", out r, fam, size, seg, ObjectId.Null, a.Bool("apply_rules", true)); }
            catch (Exception ex) { throw new InvalidOperationException(ex.Message + CatalogHint); }
            var pid = (ObjectId)r[3];
            var pipe = c.Open(pid, true);
            // An invert is the inside bottom of the pipe, but Civil 3D stores the centreline: add half the inner height.
            double half = HalfInnerHeight(pipe);
            var warnings = new List<string>();
            if (a.Has("start_invert")) Try(warnings, "start_invert", () =>
                Reflect.Set(pipe, "StartPoint", new Point3d(seg.StartPoint.X, seg.StartPoint.Y, a.Dbl("start_invert") + half)));
            if (a.Has("end_invert")) Try(warnings, "end_invert", () =>
                Reflect.Set(pipe, "EndPoint", new Point3d(seg.EndPoint.X, seg.EndPoint.Y, a.Dbl("end_invert") + half)));
            if (a.Has("slope")) Try(warnings, "slope", () => Reflect.Call(pipe, "SetSlopeHoldStart", a.Dbl("slope")));
            if (a.Has("name")) Try(warnings, "name", () => Reflect.Set(pipe, "Name", a.Str("name")));

            // Connect only after the elevations are final: a structure sets its sump from the pipe inverts at the
            // moment the pipe is connected, and does not follow later elevation changes.
            if (a.Has("start_structure")) Reflect.Call(pipe, "ConnectToStructure", "Start", c.Find(a.Str("start_structure")), true);
            if (a.Has("end_structure")) Reflect.Call(pipe, "ConnectToStructure", "End", c.Find(a.Str("end_structure")), true);

            var res = Props.Pick(pipe, "Name", "Handle", "Length2D", "Slope", "StartPoint", "EndPoint");
            var sp = (Point3d)Reflect.Get(pipe, "StartPoint");
            var ep = (Point3d)Reflect.Get(pipe, "EndPoint");
            res["start_invert"] = Math.Round(sp.Z - half, 4);
            res["end_invert"] = Math.Round(ep.Z - half, 4);
            res["falls_from_start_to_end"] = ep.Z < sp.Z;
            if (!a.Has("start_invert") && !a.Has("end_invert") && Math.Abs(sp.Z) < 1e-6 && Math.Abs(ep.Z) < 1e-6)
                warnings.Add("The pipe is at elevation 0 because no start_invert was given and no rule set its elevation. "
                    + "Pass start_invert (inside bottom of the pipe) and a slope (negative = falls from start to end).");
            if (warnings.Count > 0) res["warnings"] = warnings;
            return res;
        }

        static double HalfInnerHeight(object pipe)
        {
            foreach (var prop in new[] { "InnerHeight", "InnerDiameterOrWidth" })
            {
                try { var v = Convert.ToDouble(Reflect.Get(pipe, prop)); if (v > 0) return v / 2; } catch { }
            }
            return 0;
        }

        [Tool("pipes.connect")]
        public static object Connect(Args a, Ctx c)
        {
            var pipe = c.Open(a.NeedStr("pipe"), true);
            Reflect.Call(pipe, "ConnectToStructure", a.Str("end", "Start"), c.Find(a.NeedStr("structure")), true);
            return new { ok = true };
        }

        [Tool("pipes.network_info")]
        public static object Info(Args a, Ctx c)
        {
            var net = c.Open(a.NeedStr("network"));
            var pipes = Ctx.SafeIds(Reflect.Call(net, "GetPipeIds")).Select(id =>
            {
                var d = Props.Pick(c.Open(id), "Name", "Length2D", "Slope", "InnerDiameterOrWidth", "StartPoint", "EndPoint", "StartStructureId", "EndStructureId");
                d["handle"] = Ctx.H(id); return d;
            }).ToList();
            var structs = Ctx.SafeIds(Reflect.Call(net, "GetStructureIds")).Select(id =>
            {
                var d = Props.Pick(c.Open(id), "Name", "Location", "RimElevation", "SumpElevation", "PartSizeName");
                d["handle"] = Ctx.H(id); return d;
            }).ToList();
            return new { name = Reflect.Get(net, "Name"), pipes, structures = structs };
        }

        [Tool("pipes.apply_rules")]
        public static object ApplyRules(Args a, Ctx c)
        {
            int n = 0;
            foreach (var id in c.FindMany(a.Strs("parts"))) { Reflect.Call(c.Open(id, true), "ApplyRules"); n++; }
            return new { applied = n };
        }

        [Tool("pipes.interference_check")]
        public static object Interference(Args a, Ctx c)
        {
            c.Doc.SendStringToExecute("_AeccCreateInterferenceCheck ", true, false, true);
            return new { note = "Interference Check command started in Civil 3D. Pick the networks in the dialog." };
        }

        [Tool("pressure.network_info")]
        public static object PressureInfo(Args a, Ctx c)
        {
            var net = c.Open(a.NeedStr("network"));
            var d = Props.GetAll(net);
            foreach (var m in new[] { "GetPipeIds", "GetFittingIds", "GetAppurtenanceIds" })
                try { d[m] = Ctx.H(Reflect.Call(net, m) as System.Collections.IEnumerable); } catch { }
            return d;
        }
    }
}

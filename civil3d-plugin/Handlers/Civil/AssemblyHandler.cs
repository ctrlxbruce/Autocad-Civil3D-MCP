using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using CivilAssembly = Autodesk.Civil.DatabaseServices.Assembly;

namespace Civil3DMcp.Handlers
{
    /// <summary>
    /// Assemblies and stock subassemblies (lanes, curbs, shoulders, daylight ...), so corridors get real cross sections.
    /// </summary>
    public static class AssemblyHandler
    {
        // Common Civil 3D stock subassembly classes (names as in the Civil 3D tool catalogs).
        static readonly Dictionary<string, string[]> Stock = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Basic"] = new[] { "BasicLane", "BasicLaneTransition", "BasicCurb", "BasicCurbAndGutter", "BasicShoulder", "BasicSidewalk",
                                "BasicSideSlopeCutDitch", "BasicBarrier", "BasicGuardrail" },
            ["Lanes"] = new[] { "LaneSuperelevationAOR", "LaneOutsideSuperMultiLayer", "LaneInsideSuperMultiLayer", "LaneParabolic",
                                "LaneBrokenBack", "GenericPavementStructure", "ShapeTrapezoidal" },
            ["Shoulders"] = new[] { "ShoulderExtendAll", "ShoulderExtendSubbase", "ShoulderMultiLayer", "ShoulderVerticalSubbase" },
            ["CurbAndSidewalk"] = new[] { "UrbanCurbGutterGeneral", "UrbanCurbGutterValley1", "UrbanSidewalk" },
            ["Daylight"] = new[] { "DaylightStandard", "DaylightGeneral", "DaylightBench", "DaylightMaxWidth", "DaylightMaxOffset",
                                   "DaylightToOffset", "DaylightBasin", "DaylightRockCut", "StrippingTopSoil" },
            ["Links"] = new[] { "LinkWidthAndSlope", "LinkSlopeToSurface", "LinkOffsetAndElevation", "LinkOffsetAndSlope", "LinkVertical", "MarkPoint" },
            ["Medians"] = new[] { "MedianDepressed", "MedianRaisedWithCrown", "MedianFlushWithBarrier" },
            ["ChannelsAndTrenches"] = new[] { "Channel", "Ditch", "SideDitch", "SideDitchUShape", "TrenchPipe1", "TrenchPipe2" },
            ["RetainingWalls"] = new[] { "RetainWallVertical", "RetainWallTapered", "RetainWallToLowSide" },
            ["Conditional"] = new[] { "ConditionalCutOrFill", "ConditionalHorizontalTarget" },
        };

        static readonly string[] ParamCollections = { "ParamsDouble", "ParamsLong", "ParamsBool", "ParamsString" };

        [Tool("assembly.list_stock", ReadOnly = true)]
        public static object ListStock(Args a, Ctx c)
        {
            var cat = a.Str("category");
            var res = cat == null ? Stock : Stock.Where(kv => kv.Key.Equals(cat, StringComparison.OrdinalIgnoreCase)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return new { catalog = res, note = "Use the class name as 'type' in assembly_add_subassembly. Use assembly_subassembly_params to see a subassembly's parameter names." };
        }

        static IEnumerable<ObjectId> SubassemblyIds(Ctx c, CivilAssembly asm)
        {
            var ids = new List<ObjectId>();
            try
            {
                foreach (var group in (IEnumerable)Reflect.Get(asm, "Groups"))
                {
                    ObjectId gid = group is ObjectId g ? g : ObjectId.Null;
                    var grp = gid.IsNull ? group : c.Open(gid);
                    ids.AddRange(Ctx.SafeIds(Reflect.Call(grp, "GetSubassemblyIds")));
                }
            }
            catch { }
            return ids;
        }

        static string UniqueName(Ctx c, string baseName)
        {
            string name = baseName;
            for (int i = 2; i < 1000 && !c.FindByName(name).IsNull; i++) name = $"{baseName} {i}";
            return name;
        }

        // Each Params* collection holds parameter objects with Key / DisplayName / Value.
        static IEnumerable<(string kind, object param)> Params(object sub)
        {
            foreach (var coll in ParamCollections)
            {
                object items = null;
                try { items = Reflect.Get(sub, coll); } catch { }
                if (!(items is IEnumerable e)) continue;
                foreach (var p in e) if (p != null) yield return (coll, p);
            }
        }

        static string Key(object param)
        {
            foreach (var n in new[] { "Key", "DisplayName", "Name" })
                try { if (Reflect.Get(param, n) is string s && s.Length > 0) return s; } catch { }
            return null;
        }

        static object ConvertFor(string kind, System.Text.Json.Nodes.JsonNode value)
        {
            var raw = Conv.Unwrap(value);
            switch (kind)
            {
                case "ParamsDouble": return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                case "ParamsLong": return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                case "ParamsBool": return Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
                default: return raw?.ToString();
            }
        }

        // Point codes of a subassembly, and the point object for each code (open the subassembly for write,
        // read-only opens return an empty Points collection).
        static List<string> PointCodes(object sub, out Dictionary<string, object> pointByCode, out object firstPoint)
        {
            pointByCode = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            firstPoint = null;
            var all = new List<string>();
            object points = null;
            try { points = Reflect.Get(sub, "Points"); } catch { }
            if (!(points is IEnumerable e)) return all;
            foreach (var pt in e)
            {
                if (pt == null) continue;
                firstPoint = firstPoint ?? pt;
                object codes = null;
                try { codes = Reflect.Get(pt, "Codes"); } catch { }
                if (codes is IEnumerable ce)
                    foreach (var code in ce)
                    {
                        var s = code?.ToString();
                        if (string.IsNullOrEmpty(s)) continue;
                        if (!pointByCode.ContainsKey(s)) pointByCode[s] = pt;
                        if (!all.Contains(s)) all.Add(s);
                    }
            }
            return all;
        }

        [Tool("assembly.add_subassembly")]
        public static object AddSubassembly(Args a, Ctx c)
        {
            var asm = (CivilAssembly)c.Open(a.NeedStr("assembly"), true);
            var type = a.NeedStr("type");
            var cls = type.StartsWith("Subassembly.", StringComparison.OrdinalIgnoreCase) ? type : "Subassembly." + type;
            var shortName = cls.Substring("Subassembly.".Length);
            var side = a.Str("side", "Right");
            var sides = side.Equals("Both", StringComparison.OrdinalIgnoreCase) ? new[] { "Left", "Right" }
                      : side.Equals("Left", StringComparison.OrdinalIgnoreCase) ? new[] { "Left" }
                      : side.Equals("Right", StringComparison.OrdinalIgnoreCase) ? new[] { "Right" }
                      : throw new ArgumentException("side must be Left, Right or Both.");
            var values = a.Obj("params");

            var created = new List<object>();
            foreach (var sd in sides)
            {
                var warnings = new List<string>();
                var name = UniqueName(c, a.Str("name", shortName) + (sides.Length > 1 || !a.Has("name") ? $" ({sd})" : ""));
                ObjectId id;
                try { id = c.Civ.SubassemblyCollection.ImportStockSubassembly(name, cls, asm.Location); }
                catch (Exception ex) { throw new ArgumentException($"Could not create stock subassembly '{shortName}': {ex.Message}. Use assembly_list_stock for valid types."); }

                // Hook to a point of another subassembly (for example the outer edge of a lane), else attach to the assembly.
                if (a.Has("hook_to"))
                {
                    var hookName = a.Str("hook_to");
                    var parentId = SubassemblyIds(c, asm).FirstOrDefault(sid =>
                    {
                        var n = Reflect.Get(c.Open(sid), "Name") as string;
                        return string.Equals(n, hookName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(n, $"{hookName} ({sd})", StringComparison.OrdinalIgnoreCase);
                    });
                    if (parentId.IsNull) throw new ArgumentException($"hook_to '{hookName}' is not a subassembly of this assembly. Use assembly_info to see the names.");
                    var parent = c.Open(parentId, true);
                    var codes = PointCodes(parent, out var pointByCode, out var firstPoint);
                    object hookPoint = firstPoint;
                    if (a.Has("hook_point_code") && !pointByCode.TryGetValue(a.Str("hook_point_code"), out hookPoint))
                        throw new ArgumentException($"Point code '{a.Str("hook_point_code")}' not found on '{hookName}'. Available: {string.Join(", ", codes)}");
                    if (hookPoint == null) throw new ArgumentException($"'{hookName}' has no points to hook to.");
                    // SubassemblyHookTo is read-only; the hook is made by adding the subassembly at the parent's point.
                    asm.AddSubassembly(id, (Autodesk.Civil.DatabaseServices.Point)hookPoint);
                }
                else asm.AddSubassembly(id);

                var subObj = c.Open(id, true);
                try { Reflect.Set(subObj, "Side", sd); } catch (Exception ex) { warnings.Add("side was not set: " + ex.Message); }

                var set = new List<string>();
                var missing = new List<string>();
                if (values != null)
                {
                    var all = Params(subObj).ToList();
                    foreach (var kv in values)
                    {
                        var match = all.FirstOrDefault(p => string.Equals(Key(p.param), kv.Key, StringComparison.OrdinalIgnoreCase)
                                                         || string.Equals(Reflect.Get(p.param, "DisplayName") as string, kv.Key, StringComparison.OrdinalIgnoreCase));
                        if (match.param == null) { missing.Add(kv.Key); continue; }
                        try { Reflect.Set(match.param, "Value", ConvertFor(match.kind, kv.Value)); set.Add(kv.Key); }
                        catch (Exception ex) { warnings.Add($"{kv.Key} was not set: {(ex.InnerException ?? ex).Message}"); }
                    }
                }
                var row = new Dictionary<string, object> { ["name"] = name, ["handle"] = Ctx.H(id), ["side"] = sd, ["type"] = shortName };
                if (set.Count > 0) row["params_set"] = set;
                if (missing.Count > 0) row["params_not_found"] = missing;
                if (warnings.Count > 0) row["warnings"] = warnings;
                created.Add(row);
            }
            return new { assembly = asm.Name, created, note = "Use assembly_subassembly_params to see parameter names and point codes." };
        }

        [Tool("assembly.subassembly_params")]
        public static object SubassemblyParams(Args a, Ctx c)
        {
            var sub = c.Open(a.NeedStr("subassembly"), true);   // point codes are only filled when opened for write
            var parameters = Params(sub).Select(p => new
            {
                key = Key(p.param),
                display_name = CivilCommonHandler.Safe(() => Reflect.Get(p.param, "DisplayName") as string),
                type = p.kind.Replace("Params", "").ToLowerInvariant(),
                value = Conv.Plain(CivilCommonHandler.Safe(() => Reflect.Get(p.param, "Value"))),
            }).ToList();
            var codes = PointCodes(sub, out _, out _);
            return new { name = Reflect.Get(sub, "Name"), side = Conv.Plain(CivilCommonHandler.Safe(() => Reflect.Get(sub, "Side"))),
                         parameters, point_codes = codes };
        }

        [Tool("assembly.info", ReadOnly = true)]
        public static object Info(Args a, Ctx c)
        {
            var asm = (CivilAssembly)c.Open(a.NeedStr("assembly"));
            var subs = SubassemblyIds(c, asm).Select(id =>
            {
                var s = c.Open(id);
                return new
                {
                    name = Reflect.Get(s, "Name"), handle = Ctx.H(id),
                    side = Conv.Plain(CivilCommonHandler.Safe(() => Reflect.Get(s, "Side"))),
                    hooked_to = CivilCommonHandler.Safe(() => Reflect.Get(s, "SubassemblyHookTo") is ObjectId h && !h.IsNull ? Reflect.Get(c.Open(h), "Name") : null),
                };
            }).ToList();
            return new { name = asm.Name, handle = Ctx.H(asm.ObjectId), location = new[] { asm.Location.X, asm.Location.Y }, subassemblies = subs };
        }
    }
}

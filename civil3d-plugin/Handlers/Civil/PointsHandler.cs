using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    /// <summary>COGO points, importing point files, point groups, point & label styles, description keys.</summary>
    public static class PointsHandler
    {
        static object Points(Ctx c) => Reflect.Get(c.Civ, "CogoPoints");

        static readonly string[] PointFields =
            { "PointNumber", "PointName", "Easting", "Northing", "Elevation", "RawDescription", "FullDescription", "Latitude", "Longitude" };

        [Tool("points.create")]
        public static object Create(Args a, Ctx c)
        {
            var res = new List<object>();
            var pts = Points(c);
            foreach (var n in a.Arr("points"))
            {
                Point3d p; string desc = ""; JsonNode num = null;
                if (n is JsonObject o)
                {
                    p = Conv.ToPoint3d(o);
                    desc = Conv.NodeToString(o["desc"] ?? o["description"]) ?? "";
                    num = o["number"];
                }
                else p = Conv.ToPoint3d(n);
                var id = (ObjectId)Reflect.Call(pts, "Add", p, desc, true);
                var cp = c.Open(id, true);
                if (num != null) Reflect.Set(cp, "PointNumber", num);
                if (a.Has("style")) Reflect.Set(cp, "StyleId", c.StyleId("PointStyles", a.Str("style")));
                if (a.Has("label_style")) Reflect.Set(cp, "LabelStyleId", c.StyleId("LabelStyles.PointLabelStyles.LabelStyles", a.Str("label_style")));
                if (a.Has("layer")) { c.EnsureLayer(a.Str("layer")); ((Entity)cp).Layer = a.Str("layer"); }
                res.Add(new { handle = Ctx.H(id), number = Conv.Plain(Reflect.Get(cp, "PointNumber")) });
            }
            return new { created = res.Count, points = res };
        }

        [Tool("points.list", ReadOnly = true)]
        public static object ListPoints(Args a, Ctx c)
        {
            int limit = a.Int("limit", 500);
            IEnumerable<ObjectId> ids = Ctx.SafeIds(Points(c));
            if (a.Has("group"))
            {
                var pg = c.Open(a.Str("group"));
                var numbers = new HashSet<uint>(((IEnumerable)Reflect.Call(pg, "GetPointNumbers")).Cast<object>().Select(x => Convert.ToUInt32(x)));
                ids = ids.Where(id => numbers.Contains(Convert.ToUInt32(Reflect.Get(c.Open(id), "PointNumber"))));
            }
            var list = ids.Take(limit).Select(id =>
            {
                var d = Props.Pick(c.Open(id), PointFields);
                d["handle"] = Ctx.H(id);
                return d;
            }).ToList();
            return new { count = list.Count, points = list };
        }

        /// <summary>Find a point by its point number.</summary>
        [Tool("points.get", ReadOnly = true)]
        public static object GetPoint(Args a, Ctx c)
        {
            uint n = (uint)a.Int("number");
            var id = (ObjectId)Reflect.Call(Points(c), "GetPointByPointNumber", n);
            var d = Props.GetAll(c.Open(id));
            d["handle"] = Ctx.H(id);
            return d;
        }

        /// <summary>Edit by handles or numbers: {"RawDescription":"TREE","Elevation":101.2,"StyleId":"..."}</summary>
        [Tool("points.edit")]
        public static object Edit(Args a, Ctx c)
        {
            var ids = c.FindMany(a.Strs("handles")).ToList();
            foreach (var n in a.Dbls("numbers"))
                ids.Add((ObjectId)Reflect.Call(Points(c), "GetPointByPointNumber", (uint)n));
            var report = new Dictionary<string, object>();
            foreach (var id in ids) report[Ctx.H(id)] = Props.SetMany(c.Open(id, true), a.Obj("props"), c);
            return report;
        }

        [Tool("points.delete")]
        public static object DeletePoints(Args a, Ctx c)
        {
            int n = 0;
            foreach (var num in a.Dbls("numbers"))
            {
                var id = (ObjectId)Reflect.Call(Points(c), "GetPointByPointNumber", (uint)num);
                Reflect.Call(Points(c), "Remove", id); n++;
            }
            return new { deleted = n };
        }

        [Tool("points.list_file_formats", ReadOnly = true)]
        public static object Formats(Args a, Ctx c)
        {
            var fmts = Reflect.CallStatic(Reflect.T("PointFileFormatCollection"), "GetPointFileFormats", c.Db);
            var names = new List<string>();
            if (fmts is IEnumerable e)
                foreach (var f in e)
                {
                    if (f is ObjectId id) names.Add(CivilCommonHandler.Safe(() => Reflect.Get(c.Open(id), "Name") as string));
                    else names.Add(CivilCommonHandler.Safe(() => Reflect.Get(f, "Name") as string) ?? f.ToString());
                }
            return names;
        }

        /// <summary>
        /// Import a CSV/TXT point file. format e.g. "PNEZD (comma delimited)", "PENZD (space delimited)".
        /// Files that write numbers with a decimal comma (1234,56) and use ; tab or space between fields are
        /// converted to a temporary comma-delimited copy first (decimal_comma: auto | true | false).
        /// </summary>
        [Tool("points.import")]
        public static object Import(Args a, Ctx c)
        {
            var path = a.NeedStr("path");
            var formatName = a.Str("format", "PNEZD (comma delimited)");
            var mode = a.Str("decimal_comma", "auto").ToLowerInvariant();
            string temp = null, note = null;
            if (mode != "false" && mode != "no")
            {
                var copy = DecimalCommaCopy(path, formatName, out int converted);
                if (copy != null)
                {
                    temp = copy.Value.path;
                    formatName = copy.Value.format;
                    note = $"{converted} lines used a decimal comma; imported from a converted comma-delimited copy.";
                }
            }
            try
            {
                var fmts = Reflect.CallStatic(Reflect.T("PointFileFormatCollection"), "GetPointFileFormats", c.Db);
                var fmt = Reflect.Index(fmts, formatName);
                if (fmt is ObjectId fid) fmt = c.Open(fid);
                var cpc = Reflect.T("CogoPointCollection");
                object count;
                if (a.Has("add_to_group"))
                {
                    var groups = Reflect.Get(c.Civ, "PointGroups");
                    var gname = a.Str("add_to_group");
                    ObjectId gid;
                    try { gid = (ObjectId)Reflect.Index(groups, gname); } catch { gid = (ObjectId)Reflect.Call(groups, "Add", gname); }
                    count = Reflect.CallStatic(cpc, "ImportPoints", temp ?? path, fmt, gid);
                    try { Reflect.Call(c.Open(gid, true), "Update"); } catch { }
                }
                else count = Reflect.CallStatic(cpc, "ImportPoints", temp ?? path, fmt);
                return new { imported = Conv.Plain(count), group = a.Str("add_to_group"), format = formatName, note };
            }
            finally
            {
                if (temp != null) try { System.IO.File.Delete(temp); } catch { }
            }
        }

        static readonly System.Text.RegularExpressions.Regex CommaNumber =
            new System.Text.RegularExpressions.Regex(@"^[+-]?(\d{1,3}(\.\d{3})+|\d+),\d+$");

        // Returns a converted temp copy and its comma-delimited format name, or null when no conversion is needed.
        static (string path, string format)? DecimalCommaCopy(string path, string format, out int convertedLines)
        {
            convertedLines = 0;
            var lines = System.IO.File.ReadAllLines(path);
            var first = lines.FirstOrDefault(l => l.Trim().Length > 0);
            if (first == null) return null;
            char delim = first.Contains(';') ? ';' : first.Contains('\t') ? '\t' : first.Contains(',') ? ',' : ' ';
            if (delim == ',') return null;   // a comma-delimited file can't also use decimal commas
            var order = new string(format.TakeWhile(ch => !char.IsWhiteSpace(ch) && ch != '(').ToArray()).ToUpperInvariant();
            bool Numeric(int i) => i < order.Length && "NEZXY".IndexOf(order[i]) >= 0;
            string[] Split(string l) => delim == ' ' ? l.Split((char[])null, StringSplitOptions.RemoveEmptyEntries) : l.Split(delim);

            bool needed = lines.Any(l => Split(l).Select((t, i) => Numeric(i) && CommaNumber.IsMatch(t.Trim())).Any(x => x));
            if (!needed) return null;

            var output = new List<string>();
            int lineNo = 0;
            foreach (var raw in lines)
            {
                lineNo++;
                if (raw.Trim().Length == 0) continue;
                var tokens = Split(raw);
                bool changed = false;
                for (int i = 0; i < tokens.Length; i++)
                {
                    var t = tokens[i].Trim();
                    if (Numeric(i))
                    {
                        if (CommaNumber.IsMatch(t)) { t = t.Replace(".", "").Replace(',', '.'); changed = true; }
                        if (t.Length > 0 && !double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                            throw new ArgumentException($"Line {lineNo}: '{tokens[i].Trim()}' is not a number (field {order[i]}).");
                    }
                    else t = t.Replace(',', ' ');   // keep descriptions from adding columns
                    tokens[i] = t;
                }
                if (changed) convertedLines++;
                output.Add(string.Join(",", tokens));
            }
            var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"civil3d-mcp-points-{Guid.NewGuid():N}.csv");
            System.IO.File.WriteAllLines(temp, output);
            var commaFormat = System.Text.RegularExpressions.Regex.Replace(format, @"\(\s*\w+\s+delimited\s*\)", "(comma delimited)");
            return (temp, commaFormat);
        }

        [Tool("points.elevations_from_surface")]
        public static object ElevFromSurface(Args a, Ctx c)
        {
            var surf = c.Open(a.NeedStr("surface"));
            IEnumerable<ObjectId> ids = Ctx.SafeIds(Points(c));
            if (a.Has("numbers"))
            {
                var set = new HashSet<uint>(a.Dbls("numbers").Select(x => (uint)x));
                ids = ids.Where(id => set.Contains(Convert.ToUInt32(Reflect.Get(c.Open(id), "PointNumber"))));
            }
            int ok = 0, outside = 0;
            foreach (var id in ids)
            {
                var cp = c.Open(id, true);
                try
                {
                    var z = Reflect.Call(surf, "FindElevationAtXY", Reflect.Get(cp, "Easting"), Reflect.Get(cp, "Northing"));
                    Reflect.Set(cp, "Elevation", z); ok++;
                }
                catch { outside++; }
            }
            return new { updated = ok, outside_surface = outside };
        }

        // ---------------------------------------------------------------- groups
        /// <summary>
        /// Create/update a point group. Query fields (strings, Civil 3D syntax):
        /// include_numbers "1-100,200", include_raw_desc "TREE*,MH*", include_full_desc, include_elevations "&gt;100",
        /// include_names, exclude_numbers, exclude_raw_desc, exclude_full_desc, exclude_elevations, exclude_names, include_all (bool).
        /// </summary>
        [Tool("point_groups.create")]
        public static object CreateGroup(Args a, Ctx c)
        {
            var groups = Reflect.Get(c.Civ, "PointGroups");
            var name = a.NeedStr("name");
            ObjectId id;
            try { id = (ObjectId)Reflect.Index(groups, name); }
            catch { id = (ObjectId)Reflect.Call(groups, "Add", name); }
            var pg = c.Open(id, true);
            var q = Activator.CreateInstance(Reflect.T("StandardPointGroupQuery"));
            var map = new Dictionary<string, string>
            {
                ["include_numbers"] = "IncludeNumbers", ["include_raw_desc"] = "IncludeRawDescriptions",
                ["include_full_desc"] = "IncludeFullDescriptions", ["include_elevations"] = "IncludeElevations",
                ["include_names"] = "IncludePointNames", ["exclude_numbers"] = "ExcludeNumbers",
                ["exclude_raw_desc"] = "ExcludeRawDescriptions", ["exclude_full_desc"] = "ExcludeFullDescriptions",
                ["exclude_elevations"] = "ExcludeElevations", ["exclude_names"] = "ExcludePointNames",
                ["include_groups"] = "IncludePointGroups", ["exclude_groups"] = "ExcludePointGroups"
            };
            foreach (var kv in map) if (a.Has(kv.Key)) Reflect.Set(q, kv.Value, a.Str(kv.Key));
            if (a.Bool("include_all")) Reflect.Set(q, "UseCaseSensitiveMatch", false);
            Reflect.Call(pg, "SetQuery", q);
            if (a.Has("point_style")) TrySet(pg, new[] { "PointStyleId", "PointStyleOverrideId" }, c.StyleId("PointStyles", a.Str("point_style")));
            if (a.Has("label_style")) TrySet(pg, new[] { "PointLabelStyleId", "PointLabelStyleOverrideId" }, c.StyleId("LabelStyles.PointLabelStyles.LabelStyles", a.Str("label_style")));
            Reflect.Call(pg, "Update");
            return new { handle = Ctx.H(id), name, point_count = Conv.Plain(CivilCommonHandler.Safe(() => Reflect.Get(pg, "PointsCount"))) };
        }

        static void TrySet(object o, string[] names, object v)
        {
            foreach (var n in names) { try { Reflect.Set(o, n, v); return; } catch { } }
        }

        // ---------------------------------------------------------------- styles
        /// <summary>
        /// Point style. marker: UsePointForMarker | UseCustomMarker | UseSymbolForMarker (symbol = block name).
        /// size_mode: MarkerSizeType value (e.g. UseDrawingScale, UseFixedScale, UseSizeRelativeToScreen, UseAbsoluteUnits).
        /// </summary>
        [Tool("styles.point_style")]
        public static object PointStyle(Args a, Ctx c)
        {
            var create = CivilCommonHandler.CreateStyle(new Args(new JsonObject { ["category"] = "PointStyles", ["name"] = a.NeedStr("name") }), c);
            var id = c.StyleId("PointStyles", a.Str("name"));
            var st = c.Open(id, true);
            var report = new Dictionary<string, string>();
            void S(string key, string prop) { if (a.Has(key)) { try { Reflect.Set(st, prop, a.Node(key), c); report[key] = "ok"; } catch (Exception ex) { report[key] = ex.InnerException?.Message ?? ex.Message; } } }
            S("marker", "MarkerType");          // UsePointForMarker | UseCustomMarker | UseSymbolForMarker
            S("symbol", "MarkerSymbolName");    // block name when marker = UseSymbolForMarker
            S("block", "MarkerSymbolName");
            S("custom_marker", "CustomMarkerStyle");
            S("size_mode", "SizeType");
            S("size", "MarkerSize");
            S("rotation", "MarkerRotationAngle");
            if (a.Has("props")) foreach (var kv in Props.SetMany(st, a.Obj("props"), c)) report[kv.Key] = kv.Value;
            return new { handle = Ctx.H(id), settings = report };
        }

        // ---------------------------------------------------------------- description keys
        [Tool("points.description_keys", ReadOnly = true)]
        public static object DescKeys(Args a, Ctx c)
        {
            var t = Reflect.T("PointDescriptionKeySetCollection");
            var sets = Reflect.CallStatic(t, "GetPointDescriptionKeySets", c.Db);
            var res = new List<object>();
            foreach (var sid in Ctx.SafeIds(sets))
            {
                var set = c.Open(sid);
                var keys = new List<object>();
                foreach (var kid in Ctx.SafeIds(set)) keys.Add(Props.Pick(c.Open(kid), "Code", "Format", "StyleId", "LabelStyleId", "LayerId"));
                res.Add(new { name = Reflect.Get(set, "Name"), handle = Ctx.H(sid), keys });
            }
            return res;
        }
    }
}

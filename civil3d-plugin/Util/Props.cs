using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;

namespace Civil3DMcp
{
    /// <summary>
    /// Read or write ANY property of ANY object by name. This is how one tool covers
    /// every property you see in the Civil 3D Properties palette and style dialogs.
    /// </summary>
    public static class Props
    {
        static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        { "AcadObject", "UnmanagedObject", "AcadDatabase", "Database", "ExtensionDictionary", "OwnerId",
          "XData", "Drawable", "Bounds", "ClassID", "ObjectBirthVersion", "IsDisposed", "AutoDelete" };

        public static Dictionary<string, object> GetAll(object o, int depth = 0)
        {
            var d = new Dictionary<string, object> { ["_type"] = o.GetType().Name };
            foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetGetMethod() == null || p.GetIndexParameters().Length > 0 || Skip.Contains(p.Name)) continue;
                if (d.ContainsKey(p.Name)) continue;
                try { d[p.Name] = Conv.Plain(p.GetValue(o), depth); }
                catch { /* some properties throw for some objects; ignore */ }
            }
            return d;
        }

        public static Dictionary<string, object> Pick(object o, params string[] names)
        {
            var d = new Dictionary<string, object>();
            foreach (var n in names)
                try { d[n] = Conv.Plain(Reflect.Get(o, n)); } catch { }
            return d;
        }

        /// <summary>Set many properties: {"Layer":"C-ROAD", "ContourStyle.MajorContourInterval":5}</summary>
        public static Dictionary<string, string> SetMany(object o, JsonObject props, Ctx c)
        {
            var report = new Dictionary<string, string>();
            if (props == null) return report;
            foreach (var kv in props)
            {
                try
                {
                    // friendly common names for entities
                    if (o is Entity e && ApplyOne(e, kv.Key, kv.Value, c)) { report[kv.Key] = "ok"; continue; }
                    Reflect.SetPath(o, kv.Key, kv.Value, c);
                    report[kv.Key] = "ok";
                }
                catch (Exception ex)
                {
                    report[kv.Key] = "ERROR: " + (ex.InnerException?.Message ?? ex.Message);
                }
            }
            return report;
        }

        /// <summary>layer, color, linetype, lineweight, ltscale, transparency from tool args.</summary>
        public static void ApplyCommon(Entity e, Args a, Ctx c)
        {
            foreach (var k in new[] { "layer", "color", "linetype", "lineweight", "ltscale", "transparency" })
                if (a.Has(k)) ApplyOne(e, k, a.Node(k), c);
        }

        static bool ApplyOne(Entity e, string key, JsonNode v, Ctx c)
        {
            switch (key.ToLowerInvariant())
            {
                case "layer": c.EnsureLayer(Conv.NodeToString(v)); e.Layer = Conv.NodeToString(v); return true;
                case "color": e.Color = Conv.ToColor(v); return true;
                case "linetype":
                    var lt = Conv.NodeToString(v);
                    if (!lt.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) && !lt.Equals("ByBlock", StringComparison.OrdinalIgnoreCase))
                        c.EnsureLinetype(lt);
                    e.Linetype = lt; return true;
                case "lineweight": e.LineWeight = Conv.ToLineWeight(v); return true;
                case "ltscale": case "linetypescale": e.LinetypeScale = Conv.NodeToDouble(v); return true;
                case "transparency": e.Transparency = Conv.ToTransparency(v); return true;
            }
            return false;
        }
    }
}

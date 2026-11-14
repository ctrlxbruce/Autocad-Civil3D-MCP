using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp
{
    /// <summary>Converts JSON / plain values to AutoCAD types and back.</summary>
    public static class Conv
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------------- JSON helpers ----------------
        public static string NodeToString(JsonNode n)
        {
            if (n == null) return null;
            if (n is JsonValue v && v.TryGetValue<string>(out var s)) return s;
            return n.ToJsonString().Trim('"');
        }

        public static double NodeToDouble(JsonNode n)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue<double>(out var d)) return d;
                if (v.TryGetValue<long>(out var l)) return l;
                if (v.TryGetValue<string>(out var s)) return double.Parse(s, Inv);
            }
            throw new ArgumentException($"Expected a number, got {n?.ToJsonString()}");
        }

        /// <summary>Unwrap a JsonValue to string / double / bool; arrays & objects stay as nodes.</summary>
        public static object Unwrap(object v)
        {
            if (v is JsonValue jv)
            {
                if (jv.TryGetValue<bool>(out var b)) return b;
                if (jv.TryGetValue<double>(out var d)) return d;
                if (jv.TryGetValue<long>(out var l)) return (double)l;
                if (jv.TryGetValue<string>(out var s)) return s;
                return jv.ToJsonString();
            }
            return v;
        }

        // ---------------- geometry ----------------
        public static Point3d ToPoint3d(object n)
        {
            n = Unwrap(n);
            switch (n)
            {
                case Point3d p: return p;
                case Point2d p2: return new Point3d(p2.X, p2.Y, 0);
                case JsonArray a: return new Point3d(NodeToDouble(a[0]), NodeToDouble(a[1]), a.Count > 2 ? NodeToDouble(a[2]) : 0);
                case JsonObject o:
                    return new Point3d(NodeToDouble(o["x"]), NodeToDouble(o["y"]), o["z"] != null ? NodeToDouble(o["z"]) : 0);
                case string s:
                    var parts = s.Split(',').Select(x => double.Parse(x.Trim(), Inv)).ToArray();
                    return new Point3d(parts[0], parts[1], parts.Length > 2 ? parts[2] : 0);
                case IList<double> l: return new Point3d(l[0], l[1], l.Count > 2 ? l[2] : 0);
            }
            throw new ArgumentException("A point must be [x, y] or [x, y, z].");
        }

        public static Point2d ToPoint2d(object n) { var p = ToPoint3d(n); return new Point2d(p.X, p.Y); }
        public static Vector3d ToVector3d(object n) { var p = ToPoint3d(n); return new Vector3d(p.X, p.Y, p.Z); }

        // ---------------- colors ----------------
        /// <summary>"1".."255" (ACI), "red", "255,0,0", "bylayer", "byblock".</summary>
        public static Color ToColor(object v)
        {
            v = Unwrap(v);
            if (v is Color c) return c;
            if (v is double d) return Color.FromColorIndex(ColorMethod.ByAci, (short)d);
            var s = (v?.ToString() ?? "").Trim().ToLowerInvariant();
            if (s.StartsWith("aci:")) s = s.Substring(4);
            switch (s)
            {
                case "bylayer": return Color.FromColorIndex(ColorMethod.ByLayer, 256);
                case "byblock": return Color.FromColorIndex(ColorMethod.ByBlock, 0);
                case "red": return Color.FromColorIndex(ColorMethod.ByAci, 1);
                case "yellow": return Color.FromColorIndex(ColorMethod.ByAci, 2);
                case "green": return Color.FromColorIndex(ColorMethod.ByAci, 3);
                case "cyan": return Color.FromColorIndex(ColorMethod.ByAci, 4);
                case "blue": return Color.FromColorIndex(ColorMethod.ByAci, 5);
                case "magenta": return Color.FromColorIndex(ColorMethod.ByAci, 6);
                case "white": case "black": return Color.FromColorIndex(ColorMethod.ByAci, 7);
                case "gray": case "grey": return Color.FromColorIndex(ColorMethod.ByAci, 8);
            }
            if (s.Contains(","))
            {
                var p = s.Split(',').Select(x => byte.Parse(x.Trim(), Inv)).ToArray();
                return Color.FromRgb(p[0], p[1], p[2]);
            }
            return Color.FromColorIndex(ColorMethod.ByAci, short.Parse(s, Inv));
        }

        public static string ColorStr(Color c)
        {
            if (c.IsByLayer) return "bylayer";
            if (c.IsByBlock) return "byblock";
            if (c.ColorMethod == ColorMethod.ByAci) return c.ColorIndex.ToString(Inv);
            return $"{c.Red},{c.Green},{c.Blue}";
        }

        /// <summary>Transparency in percent 0 (solid) .. 90.</summary>
        public static Transparency ToTransparency(object v)
        {
            double pct = Convert.ToDouble(Unwrap(v), Inv);
            return new Transparency((byte)Math.Round(255 * (100 - pct) / 100.0));
        }

        /// <summary>Lineweight in mm (0.25, 0.30 ...) or enum name ("LineWeight030", "ByLayer").</summary>
        public static LineWeight ToLineWeight(object v)
        {
            v = Unwrap(v);
            if (v is double d)
            {
                int hundredths = d < 10 ? (int)Math.Round(d * 100) : (int)Math.Round(d);
                var all = Enum.GetValues(typeof(LineWeight)).Cast<LineWeight>().Where(x => (int)x >= 0).ToList();
                return all.OrderBy(x => Math.Abs((int)x - hundredths)).First();
            }
            var s = v.ToString();
            if (double.TryParse(s, NumberStyles.Float, Inv, out var dd)) return ToLineWeight(dd);
            return (LineWeight)Enum.Parse(typeof(LineWeight), s, true);
        }

        // ---------------- the big one ----------------
        /// <summary>Convert any value (JSON or native) into the type an API property/method wants.</summary>
        public static object To(object v, Type t, Ctx c)
        {
            if (t.IsByRef) t = t.GetElementType();
            var under = Nullable.GetUnderlyingType(t);
            if (under != null) { if (v == null) return null; t = under; }

            if (v == null)
            {
                if (t == typeof(ObjectId)) return ObjectId.Null;
                if (!t.IsValueType) return null;
                throw new ArgumentException($"null not allowed for {t.Name}");
            }

            if (t.IsInstanceOfType(v)) return v;
            var u = Unwrap(v);
            if (t.IsInstanceOfType(u)) return u;

            if (t == typeof(string)) return u is JsonNode jn ? jn.ToJsonString() : Convert.ToString(u, Inv);
            if (t == typeof(object)) return u;

            if (t == typeof(LineWeight)) return ToLineWeight(u);
            if (t.IsEnum)
            {
                if (u is double dn) return Enum.ToObject(t, (int)dn);
                return Enum.Parse(t, u.ToString().Replace(" ", ""), true);
            }

            if (t == typeof(bool))
            {
                if (u is double bd) return bd != 0;
                return bool.Parse(u.ToString());
            }

            if (t.IsPrimitive || t == typeof(decimal))
            {
                double d = u is string s ? double.Parse(s, Inv) : Convert.ToDouble(u, Inv);
                if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(uint) ||
                    t == typeof(ushort) || t == typeof(byte) || t == typeof(ulong) || t == typeof(sbyte))
                    d = Math.Round(d);
                return Convert.ChangeType(d, t, Inv);
            }

            if (t == typeof(ObjectId))
            {
                if (c == null) throw new ArgumentException("Cannot resolve ObjectId without context.");
                return c.Find(u.ToString());
            }
            if (t == typeof(ObjectIdCollection))
            {
                var coll = new ObjectIdCollection();
                foreach (var item in AsList(u)) coll.Add((ObjectId)To(item, typeof(ObjectId), c));
                return coll;
            }
            if (t == typeof(Point3d)) return ToPoint3d(u);
            if (t == typeof(Point2d)) return ToPoint2d(u);
            if (t == typeof(Vector3d)) return ToVector3d(u);
            if (t == typeof(Point3dCollection))
                return new Point3dCollection(AsList(u).Select(ToPoint3d).ToArray());
            if (t == typeof(Point2dCollection))
                return new Point2dCollection(AsList(u).Select(ToPoint2d).ToArray());
            if (t == typeof(Color)) return ToColor(u);
            if (t == typeof(Transparency)) return ToTransparency(u);
            if (t == typeof(Handle)) return new Handle(Convert.ToInt64(u.ToString(), 16));

            throw new ArgumentException($"Cannot convert '{u}' to {t.Name}.");
        }

        private static IEnumerable<object> AsList(object u)
        {
            if (u is JsonArray a) return a.Cast<object>();
            if (u is string) return new[] { u };
            if (u is IEnumerable e) return e.Cast<object>();
            return new[] { u };
        }

        // ---------------- back to JSON-friendly values ----------------
        public static object Plain(object v, int depth = 0)
        {
            switch (v)
            {
                case null: return null;
                case string s: return s;
                case bool b: return b;
                case ObjectId id: return id.IsNull ? null : id.Handle.ToString();
                case Handle h: return h.ToString();
                case Point3d p: return new[] { p.X, p.Y, p.Z };
                case Point2d p2: return new[] { p2.X, p2.Y };
                case Vector3d vv: return new[] { vv.X, vv.Y, vv.Z };
                case Color col: return ColorStr(col);
                case Transparency tr: return tr.IsByAlpha ? Math.Round(100 - tr.Alpha * 100.0 / 255) : (object)tr.ToString();
                case Enum e: return e.ToString();
                case Extents3d ex: return new { min = Plain(ex.MinPoint), max = Plain(ex.MaxPoint) };
                case double d: return double.IsNaN(d) || double.IsInfinity(d) ? null : d;
                case float f: return (double)f;
                case int or long or short or uint or ushort or byte or ulong or sbyte: return Convert.ToInt64(v, Inv);
                case DBObject dbo: return dbo.ObjectId.IsNull ? dbo.GetType().Name : dbo.ObjectId.Handle.ToString();
            }
            if (depth < 2 && v is IEnumerable en)
            {
                var list = new List<object>();
                foreach (var x in en) { list.Add(Plain(x, depth + 1)); if (list.Count >= 500) break; }
                return list;
            }
            if (depth < 1 && v.GetType().IsValueType == false && v.GetType().Namespace?.StartsWith("Autodesk.Civil") == true)
                return Props.GetAll(v, depth + 1);
            return v.ToString();
        }
    }
}

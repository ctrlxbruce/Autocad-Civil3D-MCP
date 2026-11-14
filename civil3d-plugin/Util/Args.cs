using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp
{
    /// <summary>Easy, forgiving access to the JSON arguments sent by the MCP server.</summary>
    public class Args
    {
        public JsonObject J { get; }
        public Args(JsonObject j) { J = j ?? new JsonObject(); }

        public bool Has(string k) => J.ContainsKey(k) && J[k] != null;
        public JsonNode Node(string k) => Has(k) ? J[k] : null;

        public string Str(string k, string d = null) => Has(k) ? Conv.NodeToString(J[k]) : d;
        public double Dbl(string k, double d = 0) => Has(k) ? Conv.NodeToDouble(J[k]) : d;
        public int Int(string k, int d = 0) => Has(k) ? (int)Math.Round(Conv.NodeToDouble(J[k])) : d;
        public bool Bool(string k, bool d = false)
        {
            if (!Has(k)) return d;
            var n = J[k];
            if (n is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
            var s = Conv.NodeToString(n).Trim().ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes" || s == "on";
        }

        /// <summary>Angle given in degrees, returned in radians.</summary>
        public double Deg(string k, double dDeg = 0) => Dbl(k, dDeg) * Math.PI / 180.0;

        public Point3d Pt(string k) => Conv.ToPoint3d(Need(k));
        public Point3d Pt(string k, Point3d d) => Has(k) ? Conv.ToPoint3d(J[k]) : d;

        public List<Point3d> Pts(string k) => Arr(k).Select(Conv.ToPoint3d).ToList();

        public List<JsonNode> Arr(string k)
        {
            if (!Has(k)) return new List<JsonNode>();
            if (J[k] is JsonArray a) return a.ToList();
            return new List<JsonNode> { J[k] };
        }

        public List<string> Strs(string k) => Arr(k).Select(Conv.NodeToString).ToList();
        public List<double> Dbls(string k) => Arr(k).Select(Conv.NodeToDouble).ToList();

        public JsonObject Obj(string k) => Has(k) ? J[k] as JsonObject : null;

        public JsonNode Need(string k)
        {
            if (!Has(k)) throw new ArgumentException($"Missing required argument '{k}'.");
            return J[k];
        }

        public string NeedStr(string k) => Conv.NodeToString(Need(k));
    }
}

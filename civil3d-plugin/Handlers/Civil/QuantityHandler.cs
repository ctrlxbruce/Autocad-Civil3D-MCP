using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;

namespace Civil3DMcp.Handlers
{
    /// <summary>Quantities (pipe lengths, structure counts, earthwork) and a simple cost estimate / BOQ.</summary>
    public static class QuantityHandler
    {
        static string PartLabel(object o)
        {
            foreach (var p in new[] { "PartSizeName", "PartDescription", "Description", "PartFamilyName" })
                try { if (Reflect.Get(o, p) is string s && s.Length > 0) return s; } catch { }
            return "(unknown part)";
        }

        static double Num(object o, params string[] names)
        {
            foreach (var n in names)
                try { var v = Convert.ToDouble(Reflect.Get(o, n)); if (!double.IsNaN(v)) return v; } catch { }
            return 0;
        }

        static IEnumerable<ObjectId> NetworkIds(Ctx c, Args a, string getter)
        {
            var all = (getter == "GetPressurePipeNetworkIds" ? c.PressureNetworkIds() : Ctx.SafeIds(Reflect.Call(c.Civ, getter))).ToList();
            if (!a.Has("networks")) return all;
            var wanted = new HashSet<string>(a.Strs("networks"), StringComparer.OrdinalIgnoreCase);
            return all.Where(id => wanted.Contains(Reflect.Get(c.Open(id), "Name") as string ?? ""));
        }

        sealed class Line { public string Network, Kind, Part; public double Length; public int Count; }

        static List<Line> Collect(Ctx c, Args a)
        {
            double f = Hydraulics.ToMeters(c.Db);
            var lines = new List<Line>();
            void Add(string net, string kind, object part, double length)
            {
                var label = PartLabel(part);
                var row = lines.FirstOrDefault(l => l.Network == net && l.Kind == kind && l.Part == label);
                if (row == null) lines.Add(row = new Line { Network = net, Kind = kind, Part = label });
                row.Count++;
                row.Length += length;
            }
            foreach (var nid in NetworkIds(c, a, "GetPipeNetworkIds"))
            {
                var net = c.Open(nid);
                var name = Reflect.Get(net, "Name") as string;
                foreach (var id in Ctx.SafeIds(Reflect.Call(net, "GetPipeIds"))) { var p = c.Open(id); Add(name, "pipe", p, Num(p, "Length2D") * f); }
                foreach (var id in Ctx.SafeIds(Reflect.Call(net, "GetStructureIds"))) Add(name, "structure", c.Open(id), 0);
            }
            foreach (var nid in NetworkIds(c, a, "GetPressurePipeNetworkIds"))
            {
                var net = c.Open(nid);
                var name = Reflect.Get(net, "Name") as string;
                foreach (var id in Ctx.SafeIds(Reflect.Call(net, "GetPipeIds"))) { var p = c.Open(id); Add(name, "pressure pipe", p, Num(p, "Length2D", "Length3D") * f); }
                foreach (var id in Ctx.SafeIds(Reflect.Call(net, "GetFittingIds"))) Add(name, "fitting", c.Open(id), 0);
                foreach (var id in Ctx.SafeIds(Reflect.Call(net, "GetAppurtenanceIds"))) Add(name, "appurtenance", c.Open(id), 0);
            }
            return lines;
        }

        [Tool("qty.pipe_lengths", ReadOnly = true)]
        public static object PipeLengths(Args a, Ctx c)
        {
            var lines = Collect(c, a);
            var rows = lines.OrderBy(l => l.Network).ThenBy(l => l.Kind).ThenBy(l => l.Part).Select(l => new
            {
                network = l.Network, kind = l.Kind, part = l.Part, count = l.Count,
                length_m = l.Kind.Contains("pipe") ? Math.Round(l.Length, 2) : (double?)null,
            }).ToList();
            var totals = lines.Where(l => l.Kind.Contains("pipe")).GroupBy(l => l.Part)
                              .Select(g => new { part = g.Key, length_m = Math.Round(g.Sum(l => l.Length), 2), pipes = g.Sum(l => l.Count) })
                              .OrderBy(t => t.part).ToList();
            return new { rows, pipe_totals_by_size = totals, note = "Pipe lengths are 2D, centre to centre." };
        }

        [Tool("qty.earthwork", ReadOnly = true)]
        public static object Earthwork(Args a, Ctx c)
        {
            var rows = new List<object>();
            double cut = 0, fill = 0;
            foreach (var id in Ctx.SafeIds(c.Civ.GetSurfaceIds()))
            {
                var s = c.Open(id);
                if (!s.GetType().Name.Contains("Volume")) continue;
                object v;
                try { v = Reflect.Call(s, "GetVolumeProperties"); } catch { continue; }
                double sc = Num(v, "AdjustedCutVolume", "UnadjustedCutVolume"), sf = Num(v, "AdjustedFillVolume", "UnadjustedFillVolume");
                cut += sc; fill += sf;
                rows.Add(new { surface = Reflect.Get(s, "Name"), cut = Math.Round(sc, 3), fill = Math.Round(sf, 3), net = Math.Round(sf - sc, 3),
                               cut_factor = Num(v, "CutFactor"), fill_factor = Num(v, "FillFactor") });
            }
            return new
            {
                volume_surfaces = rows, total_cut = Math.Round(cut, 3), total_fill = Math.Round(fill, 3), net_fill = Math.Round(fill - cut, 3),
                note = rows.Count == 0 ? "No volume surfaces. Create one with surface_volume (base and comparison surfaces)." : "Volumes in cubic drawing units (m³ in a metric drawing); net = fill - cut.",
            };
        }

        [Tool("qty.cost_estimate", NoPreview = "can write a CSV file (it doesn't change the drawing, so it can simply be run)")]
        public static object CostEstimate(Args a, Ctx c)
        {
            var rows = new List<(string item, double qty, string unit, double rate)>();
            foreach (var node in a.Arr("items"))
            {
                var it = new Args(node as System.Text.Json.Nodes.JsonObject);
                rows.Add((it.NeedStr("item"), it.Dbl("quantity"), it.Str("unit", "nos"), it.Dbl("rate")));
            }
            var rates = a.Obj("rates");
            var unpriced = new List<string>();
            if (rates != null && a.Bool("from_networks", true))
            {
                var rateMap = rates.ToDictionary(kv => kv.Key, kv => Conv.NodeToDouble(kv.Value), StringComparer.OrdinalIgnoreCase);
                foreach (var g in Collect(c, a).GroupBy(l => (l.Kind.Contains("pipe"), l.Part)))
                {
                    bool isPipe = g.Key.Item1;
                    double qty = isPipe ? g.Sum(l => l.Length) : g.Sum(l => l.Count);
                    if (rateMap.TryGetValue(g.Key.Part, out var rate)) rows.Add((g.Key.Part, Math.Round(qty, 2), isPipe ? "m" : "nos", rate));
                    else unpriced.Add(g.Key.Part);
                }
            }
            if (rows.Count == 0) throw new ArgumentException("Nothing to price: give items [{item, quantity, unit, rate}] and/or rates {part name: rate} to price the pipe networks.");

            var boq = rows.Select((r, i) => new { no = i + 1, r.item, quantity = r.qty, r.unit, r.rate, amount = Math.Round(r.qty * r.rate, 2) }).ToList();
            double total = boq.Sum(r => r.amount);
            string csv = null;
            if (a.Has("csv"))
            {
                csv = a.Str("csv");
                if (!csv.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("csv must be a path ending in .csv");
                var sb = new StringBuilder("No,Item,Quantity,Unit,Rate,Amount\r\n");
                string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
                foreach (var r in boq)
                    sb.Append(string.Join(",", r.no, Q(r.item), r.quantity.ToString(CultureInfo.InvariantCulture), Q(r.unit),
                                          r.rate.ToString(CultureInfo.InvariantCulture), r.amount.ToString(CultureInfo.InvariantCulture))).Append("\r\n");
                sb.Append($",{Q("Total")},,,,{total.ToString(CultureInfo.InvariantCulture)}\r\n");
                File.WriteAllText(csv, sb.ToString(), new UTF8Encoding(true));
            }
            return new { currency = a.Str("currency", ""), boq, total = Math.Round(total, 2), csv,
                         parts_without_rate = unpriced.Count > 0 ? unpriced : null };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    /// <summary>A short, size-capped picture of a drawing, so large real drawings fit in the chat.</summary>
    public static class SummaryHandler
    {
        static int CountOf(Func<object> f)
        {
            try { return Ctx.SafeIds(f()).Count(); } catch { return 0; }
        }

        [Tool("doc.summary")]
        public static object Summary(Args a, Ctx c)
        {
            int grid = Math.Max(1, Math.Min(a.Int("grid", 4), 10));
            int top = Math.Max(1, Math.Min(a.Int("top", 15), 100));
            var byType = new Dictionary<string, int>();
            var byLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var centres = new List<Point2d>();
            Extents3d? ext = null;
            int total = 0;

            foreach (ObjectId id in c.ModelSpace())
            {
                total++;
                var type = id.ObjectClass.DxfName;
                byType[type] = byType.TryGetValue(type, out var n) ? n + 1 : 1;
                if (!(c.Open(id) is Entity e)) continue;
                byLayer[e.Layer] = byLayer.TryGetValue(e.Layer, out var m) ? m + 1 : 1;
                try
                {
                    var ge = e.GeometricExtents;
                    if (ge.MaxPoint.X - ge.MinPoint.X > 1e9) continue;   // skip xlines/rays
                    centres.Add(new Point2d((ge.MinPoint.X + ge.MaxPoint.X) / 2, (ge.MinPoint.Y + ge.MaxPoint.Y) / 2));
                    if (ext == null) ext = ge; else { var x = ext.Value; x.AddExtents(ge); ext = x; }
                }
                catch { }
            }

            object gridCounts = null;
            if (ext != null && centres.Count > 0)
            {
                var lo = ext.Value.MinPoint; var hi = ext.Value.MaxPoint;
                double w = Math.Max(hi.X - lo.X, 1e-9), h = Math.Max(hi.Y - lo.Y, 1e-9);
                var cells = new int[grid, grid];
                foreach (var p in centres)
                {
                    int i = Math.Min(grid - 1, (int)((p.X - lo.X) / w * grid));
                    int j = Math.Min(grid - 1, (int)((p.Y - lo.Y) / h * grid));
                    cells[grid - 1 - j, i]++;
                }
                gridCounts = Enumerable.Range(0, grid).Select(r => Enumerable.Range(0, grid).Select(col => cells[r, col]).ToArray()).ToArray();
            }

            var civ = c.Civ;
            int profiles = 0;
            foreach (var aid in Ctx.SafeIds(CivilCommonHandler.Safe(() => Reflect.Call(civ, "GetAlignmentIds"))))
                profiles += CountOf(() => Reflect.Call(c.Open(aid), "GetProfileIds"));
            int pipes = 0, structures = 0;
            foreach (var nid in Ctx.SafeIds(CivilCommonHandler.Safe(() => Reflect.Call(civ, "GetPipeNetworkIds"))))
            {
                var net = c.Open(nid);
                pipes += CountOf(() => Reflect.Call(net, "GetPipeIds"));
                structures += CountOf(() => Reflect.Call(net, "GetStructureIds"));
            }

            var xrefs = new List<string>();
            var bt = (BlockTable)c.Open(c.Db.BlockTableId);
            foreach (ObjectId bid in bt)
            {
                var btr = (BlockTableRecord)c.Open(bid);
                if (btr.IsFromExternalReference) xrefs.Add(btr.Name);
            }
            var layouts = new List<string>();
            var ld = (DBDictionary)c.Open(c.Db.LayoutDictionaryId);
            foreach (DBDictionaryEntry en in ld) layouts.Add(en.Key);

            return new
            {
                drawing = System.IO.Path.GetFileName(c.Doc.Name),
                units = c.Db.Insunits.ToString(),
                entities = total,
                extents = ext == null ? null : new { min = new[] { Math.Round(ext.Value.MinPoint.X, 3), Math.Round(ext.Value.MinPoint.Y, 3) },
                                                     max = new[] { Math.Round(ext.Value.MaxPoint.X, 3), Math.Round(ext.Value.MaxPoint.Y, 3) } },
                types = byType.OrderByDescending(kv => kv.Value).Take(top).ToDictionary(kv => kv.Key, kv => kv.Value),
                layers_total = byLayer.Count,
                busiest_layers = byLayer.OrderByDescending(kv => kv.Value).Take(top).ToDictionary(kv => kv.Key, kv => kv.Value),
                spatial_grid = gridCounts,
                civil = new
                {
                    surfaces = CountOf(() => civ.GetSurfaceIds()),
                    alignments = CountOf(() => Reflect.Call(civ, "GetAlignmentIds")),
                    profiles,
                    corridors = CountOf(() => Reflect.Get(civ, "CorridorCollection")),
                    assemblies = CountOf(() => Reflect.Get(civ, "AssemblyCollection")),
                    pipe_networks = CountOf(() => Reflect.Call(civ, "GetPipeNetworkIds")), pipes, structures,
                    pressure_networks = c.PressureNetworkIds().Count(),
                    point_groups = CountOf(() => Reflect.Get(civ, "PointGroups")),
                    cogo_points = CountOf(() => Reflect.Get(civ, "CogoPoints")),
                    sites = CountOf(() => Reflect.Call(civ, "GetSiteIds")),
                },
                xrefs, layouts,
                note = $"Top {top} types and layers; spatial_grid counts objects per cell ({grid}x{grid}, first row = north). Use query_list with a layer or type filter for details.",
            };
        }
    }
}

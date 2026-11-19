using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace Civil3DMcp.Handlers
{
    public static class LayerHandler
    {
        [Tool("layers.list", ReadOnly = true)]
        public static object ListLayers(Args a, Ctx c)
        {
            var lt = c.Open<LayerTable>(c.Db.LayerTableId);
            return lt.Cast<ObjectId>().Select(id => c.Open<LayerTableRecord>(id)).Select(l => new
            {
                name = l.Name, color = Conv.ColorStr(l.Color),
                linetype = c.Open<LinetypeTableRecord>(l.LinetypeObjectId).Name,
                lineweight = l.LineWeight.ToString(), on = !l.IsOff, frozen = l.IsFrozen,
                locked = l.IsLocked, plot = l.IsPlottable, description = l.Description,
                current = l.ObjectId == c.Db.Clayer
            }).ToList();
        }

        /// <summary>Create the layer if missing, then apply any given settings.</summary>
        [Tool("layers.set")]
        public static object SetLayer(Args a, Ctx c)
        {
            var name = a.NeedStr("name");
            var id = c.EnsureLayer(name);
            var l = c.Open<LayerTableRecord>(id, true);
            if (a.Has("new_name")) l.Name = a.Str("new_name");
            if (a.Has("color")) l.Color = Conv.ToColor(a.Node("color"));
            if (a.Has("linetype")) l.LinetypeObjectId = c.EnsureLinetype(a.Str("linetype"));
            if (a.Has("lineweight")) l.LineWeight = Conv.ToLineWeight(a.Node("lineweight"));
            if (a.Has("on")) l.IsOff = !a.Bool("on");
            if (a.Has("frozen")) { if (id == c.Db.Clayer && a.Bool("frozen")) throw new InvalidOperationException("Cannot freeze the current layer."); l.IsFrozen = a.Bool("frozen"); }
            if (a.Has("locked")) l.IsLocked = a.Bool("locked");
            if (a.Has("plot")) l.IsPlottable = a.Bool("plot");
            if (a.Has("description")) l.Description = a.Str("description");
            if (a.Has("transparency")) l.Transparency = Conv.ToTransparency(a.Node("transparency"));
            if (a.Bool("make_current")) c.Db.Clayer = id;
            return new { name = l.Name, handle = Ctx.H(id) };
        }

        [Tool("layers.set_current")]
        public static object SetCurrent(Args a, Ctx c) { c.Db.Clayer = c.EnsureLayer(a.NeedStr("name")); return new { current = a.Str("name") }; }

        [Tool("layers.delete")]
        public static object DeleteLayer(Args a, Ctx c)
        {
            var lt = c.Open<LayerTable>(c.Db.LayerTableId);
            var name = a.NeedStr("name");
            if (!lt.Has(name)) return new { deleted = false, reason = "not found" };
            var ids = new ObjectIdCollection { lt[name] };
            c.Db.Purge(ids);
            if (ids.Count == 0) return new { deleted = false, reason = "layer is in use or current (move objects off it first)" };
            c.Open(lt[name], true).Erase();
            return new { deleted = true };
        }

        [Tool("linetypes.load")]
        public static object LoadLinetype(Args a, Ctx c)
        {
            var res = new Dictionary<string, string>();
            foreach (var n in a.Strs("names")) { try { c.EnsureLinetype(n); res[n] = "ok"; } catch (Exception ex) { res[n] = ex.Message; } }
            if (a.Has("ltscale")) c.Db.Ltscale = a.Dbl("ltscale");
            return res;
        }

        [Tool("linetypes.list", ReadOnly = true)]
        public static object ListLinetypes(Args a, Ctx c) =>
            c.Open<LinetypeTable>(c.Db.LinetypeTableId).Cast<ObjectId>().Select(id => c.Open<LinetypeTableRecord>(id).Name).ToList();
    }
}

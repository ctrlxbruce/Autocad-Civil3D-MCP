using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    public static class BlockHandler
    {
        [Tool("blocks.list", ReadOnly = true)]
        public static object ListBlocks(Args a, Ctx c)
        {
            var bt = c.Open<BlockTable>(c.Db.BlockTableId);
            return bt.Cast<ObjectId>().Select(id => c.Open<BlockTableRecord>(id))
                .Where(b => !b.IsLayout && !b.IsAnonymous && !b.Name.StartsWith("_") && !b.Name.StartsWith("*"))
                .Select(b => new
                {
                    name = b.Name, is_xref = b.IsFromExternalReference, is_dynamic = b.IsDynamicBlock,
                    attributes = b.Cast<ObjectId>().Select(i => c.Open(i)).OfType<AttributeDefinition>().Select(d => d.Tag).ToList()
                }).ToList();
        }

        /// <summary>Make a block from existing objects.</summary>
        [Tool("blocks.create")]
        public static object CreateBlock(Args a, Ctx c)
        {
            var name = a.NeedStr("name");
            var bt = c.Open<BlockTable>(c.Db.BlockTableId, true);
            if (bt.Has(name)) throw new ArgumentException($"Block '{name}' already exists.");
            var btr = new BlockTableRecord { Name = name, Origin = a.Pt("base_point", Point3d.Origin) };
            var btrId = bt.Add(btr);
            c.Tr.AddNewlyCreatedDBObject(btr, true);
            var ids = new ObjectIdCollection(c.FindMany(a.Strs("handles")).ToArray());
            var map = new IdMapping();
            c.Db.DeepCloneObjects(ids, btrId, map, false);
            if (a.Bool("erase_source", true)) foreach (ObjectId id in ids) c.Open(id, true).Erase();
            return new { name, handle = Ctx.H(btrId) };
        }

        [Tool("blocks.insert")]
        public static object Insert(Args a, Ctx c)
        {
            var name = a.NeedStr("name");
            var bt = c.Open<BlockTable>(c.Db.BlockTableId);
            if (!bt.Has(name)) throw new ArgumentException($"Block '{name}' not in drawing. Use blocks.import_dwg first.");
            var defId = bt[name];
            double s = a.Dbl("scale", 1);
            var br = new BlockReference(a.Pt("position"), defId)
            {
                ScaleFactors = new Scale3d(a.Dbl("scale_x", s), a.Dbl("scale_y", s), a.Dbl("scale_z", s)),
                Rotation = a.Deg("rotation")
            };
            var id = c.Add(br, a);
            var values = a.Obj("attributes");
            var def = c.Open<BlockTableRecord>(defId);
            foreach (ObjectId eid in def)
            {
                if (!(c.Open(eid) is AttributeDefinition ad) || ad.Constant) continue;
                var ar = new AttributeReference();
                ar.SetAttributeFromBlock(ad, br.BlockTransform);
                if (values != null && values.ContainsKey(ad.Tag)) ar.TextString = Conv.NodeToString(values[ad.Tag]);
                br.AttributeCollection.AppendAttribute(ar);
                c.Tr.AddNewlyCreatedDBObject(ar, true);
            }
            return new { handle = Ctx.H(id) };
        }

        [Tool("blocks.set_attributes")]
        public static object SetAttributes(Args a, Ctx c)
        {
            var br = (BlockReference)c.Open(a.NeedStr("handle"));
            var values = a.Obj("attributes");
            int n = 0;
            foreach (ObjectId id in br.AttributeCollection)
            {
                var ar = (AttributeReference)c.Open(id, true);
                if (values.ContainsKey(ar.Tag)) { ar.TextString = Conv.NodeToString(values[ar.Tag]); n++; }
            }
            return new { updated = n };
        }

        [Tool("blocks.set_dynamic_property")]
        public static object SetDynamic(Args a, Ctx c)
        {
            var br = (BlockReference)c.Open(a.NeedStr("handle"), true);
            var report = new Dictionary<string, string>();
            foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
            {
                var vals = a.Obj("props");
                if (vals == null || !vals.ContainsKey(p.PropertyName)) continue;
                try { p.Value = Conv.To(vals[p.PropertyName], p.Value.GetType(), c); report[p.PropertyName] = "ok"; }
                catch (Exception ex) { report[p.PropertyName] = ex.Message; }
            }
            return report;
        }

        /// <summary>Bring a block in from another DWG file (whole file becomes one block).</summary>
        [Tool("blocks.import_dwg")]
        public static object ImportDwg(Args a, Ctx c)
        {
            var path = a.NeedStr("path");
            var name = a.Str("name", System.IO.Path.GetFileNameWithoutExtension(path));
            using var side = new Database(false, true);
            side.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
            var id = c.Db.Insert(name, side, true);
            return new { name, handle = Ctx.H(id) };
        }

        [Tool("blocks.attach_xref", NoPreview = "attaches an external drawing")]
        public static object AttachXref(Args a, Ctx c)
        {
            var path = a.NeedStr("path");
            var name = a.Str("name", System.IO.Path.GetFileNameWithoutExtension(path));
            var xid = c.Db.AttachXref(path, name);
            var br = new BlockReference(a.Pt("position", Point3d.Origin), xid);
            return new { handle = Ctx.H(c.Add(br, a)) };
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;

namespace Civil3DMcp
{
    /// <summary>Everything a tool needs: the drawing, the open transaction and helpers.</summary>
    public class Ctx
    {
        public Document Doc { get; }
        public Database Db { get; }
        public Transaction Tr { get; }
        public Editor Ed => Doc.Editor;

        private CivilDocument _civ;
        public CivilDocument Civ => _civ ??= CivilApplication.ActiveDocument;

        public Ctx(Document doc, Transaction tr) { Doc = doc; Db = doc.Database; Tr = tr; }

        // ---------- opening objects ----------
        public DBObject Open(ObjectId id, bool write = false) =>
            Tr.GetObject(id, write ? OpenMode.ForWrite : OpenMode.ForRead, false, true);

        public T Open<T>(ObjectId id, bool write = false) where T : DBObject => (T)Open(id, write);

        public DBObject Open(string handleOrName, bool write = false) => Open(Find(handleOrName), write);

        /// <summary>The current space; read-only tools pass write: false (opening for write needs a document lock,
        /// and every lock is an undo step).</summary>
        public BlockTableRecord CurrentSpace(bool write = true) => Open<BlockTableRecord>(Db.CurrentSpaceId, write);

        public BlockTableRecord ModelSpace(bool write = true)
        {
            var bt = Open<BlockTable>(Db.BlockTableId);
            return Open<BlockTableRecord>(bt[BlockTableRecord.ModelSpace], write);
        }

        /// <summary>Add an entity to the current space and apply layer/color/etc from args.</summary>
        public ObjectId Add(Entity e, Args a = null)
        {
            var space = CurrentSpace();
            e.SetDatabaseDefaults(Db);
            var id = space.AppendEntity(e);
            Tr.AddNewlyCreatedDBObject(e, true);
            if (a != null) Props.ApplyCommon(e, a, this);
            return id;
        }

        /// <summary>Queue text for the command line. Always use this instead of Doc.SendStringToExecute, so the
        /// next tool call waits until the command has run (see UndoTracker.Busy).</summary>
        public void Send(string text, bool activate, bool wrapUpInactiveDoc, bool echo)
        {
            UndoTracker.Queued(Doc, text);
            Doc.SendStringToExecute(text, activate, wrapUpInactiveDoc, echo);
        }

        public static string H(ObjectId id) => id.IsNull ? null : id.Handle.ToString();
        public static List<string> H(IEnumerable ids) => ids.Cast<ObjectId>().Select(H).ToList();

        // ---------- finding objects by handle or by name ----------
        public ObjectId FromHandle(string hex)
        {
            hex = hex.Trim().TrimStart('#');
            long v = Convert.ToInt64(hex, 16);
            return Db.GetObjectId(false, new Handle(v), 0);
        }

        public ObjectId TryHandle(string s)
        {
            try
            {
                var id = FromHandle(s);
                if (id.IsValid && !id.IsErased) return id;
            }
            catch { }
            return ObjectId.Null;
        }

        /// <summary>
        /// Accepts a handle ("2A3F" or "#2A3F") or the name of a Civil 3D object
        /// (surface, alignment, profile, corridor, point group, pipe network, ...).
        /// </summary>
        public ObjectId Find(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return ObjectId.Null;
            if (s.StartsWith("#")) return FromHandle(s);
            var id = TryHandle(s);
            if (!id.IsNull) return id;
            id = FindByName(s);
            if (!id.IsNull) return id;
            throw new ArgumentException($"Nothing found with handle or name '{s}'.");
        }

        public List<ObjectId> FindMany(IEnumerable<string> items) => items.Select(Find).ToList();

        public ObjectId FindByName(string name)
        {
            // Civil objects that are not in model space
            foreach (var coll in new[] { "PointGroups" })
            {
                var found = MatchName(SafeIds(Reflect.Get(Civ, coll)), name);
                if (!found.IsNull) return found;
            }
            foreach (var getter in new[] { "GetPipeNetworkIds", "GetSiteIds" })
            {
                object ids = null;
                try { ids = Reflect.Call(Civ, getter); } catch { }
                var found = MatchName(SafeIds(ids), name);
                if (!found.IsNull) return found;
            }
            {
                var found = MatchName(PressureNetworkIds(), name);
                if (!found.IsNull) return found;
            }
            // Model space: anything with a Name property (surfaces, alignments, profiles, corridors ...)
            foreach (ObjectId id in ModelSpace())
            {
                if (!id.ObjectClass.DxfName.StartsWith("AECC", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var o = Open(id);
                    if (string.Equals(Reflect.Get(o, "Name") as string, name, StringComparison.OrdinalIgnoreCase)) return id;
                }
                catch { }
            }
            // Profiles live under alignments
            foreach (var aid in SafeIds(Reflect.Call(Civ, "GetAlignmentIds")))
            {
                try
                {
                    var al = Open(aid);
                    var found = MatchName(SafeIds(Reflect.Call(al, "GetProfileIds")), name);
                    if (!found.IsNull) return found;
                }
                catch { }
            }
            return ObjectId.Null;
        }

        /// <summary>Pressure networks. GetPressurePipeNetworkIds is an extension method (AeccPressurePipesMgd), so reflection on the document can't see it.</summary>
        public IEnumerable<ObjectId> PressureNetworkIds()
        {
            try { return SafeIds(CivilDocumentPressurePipesExtension.GetPressurePipeNetworkIds(Civ)); }
            catch { return Enumerable.Empty<ObjectId>(); }
        }

        private ObjectId MatchName(IEnumerable<ObjectId> ids, string name)
        {
            foreach (var id in ids)
            {
                try
                {
                    var o = Open(id);
                    if (string.Equals(Reflect.Get(o, "Name") as string, name, StringComparison.OrdinalIgnoreCase)) return id;
                }
                catch { }
            }
            return ObjectId.Null;
        }

        public static IEnumerable<ObjectId> SafeIds(object coll)
        {
            if (coll == null) return Enumerable.Empty<ObjectId>();
            if (coll is IEnumerable<ObjectId> typed) return typed.ToList();
            // Civil 3D style collections: ToObjectIds() or Count + Item[int]
            var toIds = coll.GetType().GetMethod("ToObjectIds", Type.EmptyTypes);
            if (toIds != null && toIds.Invoke(coll, null) is ObjectIdCollection oc) return oc.Cast<ObjectId>().ToList();
            var list = new List<ObjectId>();
            if (coll is IEnumerable e)
                foreach (var x in e) if (x is ObjectId id) list.Add(id);
            if (list.Count == 0)
            {
                var countProp = coll.GetType().GetProperty("Count");
                var item = coll.GetType().GetProperty("Item", new[] { typeof(int) });
                if (countProp != null && item != null)
                {
                    int n = Convert.ToInt32(countProp.GetValue(coll));
                    for (int i = 0; i < n; i++) if (item.GetValue(coll, new object[] { i }) is ObjectId id) list.Add(id);
                }
            }
            return list;
        }

        // ---------- styles ----------
        /// <summary>
        /// Find a Civil 3D style by name inside a style collection on CivilDocument.Styles.
        /// category examples: "SurfaceStyles", "PointStyles", "AlignmentStyles",
        /// "LabelSetStyles.AlignmentLabelSetStyles", "LabelStyles.PointLabelStyles.LabelStyles".
        /// If name is empty, returns the first style (the default).
        /// </summary>
        public ObjectId StyleId(string category, string name)
        {
            var coll = Reflect.GetPath(Civ.Styles, category);
            ObjectId first = ObjectId.Null;
            foreach (var id in SafeIds(coll))
            {
                if (first.IsNull) first = id;
                if (string.IsNullOrEmpty(name)) return id;
                try
                {
                    if (string.Equals(Reflect.Get(Open(id), "Name") as string, name, StringComparison.OrdinalIgnoreCase)) return id;
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(name))
                throw new ArgumentException($"Style '{name}' not found in {category}.");
            return first;
        }

        public List<string> StyleNames(string category)
        {
            var list = new List<string>();
            foreach (var id in SafeIds(Reflect.GetPath(Civ.Styles, category)))
                try { list.Add(Reflect.Get(Open(id), "Name") as string); } catch { }
            return list;
        }

        // ---------- symbol tables ----------
        public ObjectId EnsureLayer(string name)
        {
            var lt = Open<LayerTable>(Db.LayerTableId);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name };
            var id = lt.Add(rec);
            Tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        public ObjectId EnsureLinetype(string name)
        {
            var ltt = Open<LinetypeTable>(Db.LinetypeTableId);
            if (ltt.Has(name)) return ltt[name];
            foreach (var file in new[] { "acad.lin", "acadiso.lin" })
            {
                try { Db.LoadLineTypeFile(name, file); } catch { }
                ltt = Open<LinetypeTable>(Db.LinetypeTableId);
                if (ltt.Has(name)) return ltt[name];
            }
            throw new ArgumentException($"Linetype '{name}' not found in acad.lin / acadiso.lin.");
        }

        public ObjectId TextStyleId(string name)
        {
            if (string.IsNullOrEmpty(name)) return Db.Textstyle;
            var t = Open<TextStyleTable>(Db.TextStyleTableId);
            if (!t.Has(name)) throw new ArgumentException($"Text style '{name}' not found.");
            return t[name];
        }

        public ObjectId DimStyleId(string name)
        {
            if (string.IsNullOrEmpty(name)) return Db.Dimstyle;
            var t = Open<DimStyleTable>(Db.DimStyleTableId);
            if (!t.Has(name)) throw new ArgumentException($"Dimension style '{name}' not found.");
            return t[name];
        }
    }
}

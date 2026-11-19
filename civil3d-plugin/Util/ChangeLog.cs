using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;

namespace Civil3DMcp
{
    /// <summary>
    /// Preview (dry run) support. Router runs the tool inside its transaction, calls Describe() before aborting
    /// it, and returns Report(). Database events can't be used: Civil 3D sends them when a transaction commits,
    /// and a preview never commits. Instead:
    ///  - added: every handle given out while the tool ran (Database.Handseed before and after), which also
    ///    catches objects Civil 3D creates internally (a TIN surface, for example);
    ///  - changed / deleted: objects the transaction opened for write whose IsModified / IsErased flag is set.
    /// Only used on Civil 3D's main thread.
    /// </summary>
    internal sealed class ChangeLog
    {
        const int MaxItems = 25;
        const long MaxNewHandles = 50000;
        readonly Database _db;
        readonly long _seed0;
        readonly List<long> _added = new();
        JsonObject _wouldAdd, _wouldChange, _wouldDelete, _bookkeeping;

        public ChangeLog(Database db)
        {
            _db = db;
            _seed0 = db.Handseed.Value;
        }

        /// <summary>Record what the tool did. Call after it ran, before the transaction is aborted.</summary>
        public void Describe(Transaction tr)
        {
            var books = new Dictionary<string, int>();
            var added = new List<ObjectId>();
            long seed1 = _db.Handseed.Value;
            for (long h = _seed0; h < seed1 && h < _seed0 + MaxNewHandles; h++)
            {
                if (_db.TryGetObjectId(new Handle(h), out var id) && !id.IsErased)
                {
                    added.Add(id);
                    _added.Add(h);
                }
            }
            var changed = new List<ObjectId>();
            var erased = new List<ObjectId>();
            foreach (DBObject o in _db.TransactionManager.GetAllObjects())
            {
                try
                {
                    if (o.Handle.Value >= _seed0 || !o.IsWriteEnabled) continue;   // new, or only read
                    if (o.IsErased) erased.Add(o.ObjectId);
                    else if (o.IsModified) changed.Add(o.ObjectId);
                }
                catch { }
            }
            _wouldAdd = Group(tr, added, false, books);
            _wouldChange = Group(tr, changed, true, books);
            _wouldDelete = Group(tr, erased, true, books);
            _bookkeeping = new JsonObject();
            foreach (var kv in books.OrderByDescending(k => k.Value)) _bookkeeping[kv.Key] = kv.Value;
        }

        static JsonObject Group(Transaction tr, List<ObjectId> ids, bool withHandle, Dictionary<string, int> books)
        {
            var byType = new Dictionary<string, int>();
            var items = new JsonArray();
            int count = 0;
            foreach (var id in ids)
            {
                DBObject obj = null;
                try { obj = tr.GetObject(id, OpenMode.ForRead, true, true); } catch { }
                string type = obj?.GetType().Name ?? id.ObjectClass?.Name ?? "?";
                if (obj != null && IsBookkeeping(obj))
                {
                    books[type] = books.TryGetValue(type, out var b) ? b + 1 : 1;
                    continue;
                }
                count++;
                byType[type] = byType.TryGetValue(type, out var n) ? n + 1 : 1;
                if (items.Count >= MaxItems) continue;
                var item = new JsonObject { ["type"] = type };
                if (withHandle) item["handle"] = id.Handle.ToString();
                if (obj is Entity e) try { item["layer"] = e.Layer; } catch { }
                var name = NameOf(obj);
                if (!string.IsNullOrEmpty(name)) item["name"] = name;
                items.Add(item);
            }
            var g = new JsonObject { ["count"] = count };
            if (count == 0) return g;
            var types = new JsonObject();
            foreach (var kv in byType.OrderByDescending(k => k.Value)) types[kv.Key] = kv.Value;
            g["by_type"] = types;
            g["items"] = items;
            if (count > items.Count) g["more"] = count - items.Count;
            return g;
        }

        // Tables, dictionaries and model/paper space change whenever something is added to them; listed apart.
        static bool IsBookkeeping(DBObject o) =>
            o is SymbolTable || o is DBDictionary || (o is BlockTableRecord btr && btr.IsLayout);

        static string NameOf(DBObject o)
        {
            if (o == null) return null;
            if (o is SymbolTableRecord str) return str.Name;
            try { return o.GetType().GetProperty("Name")?.GetValue(o) as string; } catch { return null; }
        }

        /// <summary>The preview reply. Call after the transaction was aborted.</summary>
        public JsonObject Report(object toolResult)
        {
            // Make sure the abort really undid everything that was added.
            int kept = _added.Count(h => _db.TryGetObjectId(new Handle(h), out var id) && !id.IsErased);
            JsonNode res;
            try { res = JsonSerializer.SerializeToNode(toolResult); } catch { res = JsonValue.Create(toolResult?.ToString()); }
            var r = new JsonObject
            {
                ["preview"] = true,
                ["nothing_changed"] = kept == 0,
                ["would_add"] = _wouldAdd,
                ["would_change"] = _wouldChange,
                ["would_delete"] = _wouldDelete,
            };
            if (_bookkeeping.Count > 0) r["also_updates"] = _bookkeeping;
            r["tool_result"] = res;
            r["note"] = kept == 0
                ? "This was a preview: everything was rolled back. Handles in tool_result don't exist. Changes Civil "
                  + "3D makes internally (for example when a surface or corridor rebuilds) may not be listed. Run "
                  + "the tool itself to make these changes."
                : $"Warning: {kept} new object(s) were not rolled back. Tell the user and check the drawing (UNDO).";
            return r;
        }
    }
}

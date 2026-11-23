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
        readonly Dictionary<string, string> _header0;
        readonly JsonObject _settings = new();
        readonly List<string> _newHandles = new(), _deletedHandles = new();
        readonly List<string> _problems = new();

        public ChangeLog(Database db, Transaction tr)
        {
            _db = db;
            _seed0 = db.Handseed.Value;
            _header0 = Header(db, tr);
        }

        /// <summary>Current drawing settings a tool may change without touching any object (header variables).</summary>
        static Dictionary<string, string> Header(Database db, Transaction tr)
        {
            var h = new Dictionary<string, string>();
            void Add(string key, Func<string> f) { try { h[key] = f(); } catch { } }
            string Name(ObjectId id) => id.IsNull ? "" : NameOf(tr.GetObject(id, OpenMode.ForRead, false, true)) ?? id.Handle.ToString();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Add("current layer", () => Name(db.Clayer));
            Add("text style", () => Name(db.Textstyle));
            Add("dimension style", () => Name(db.Dimstyle));
            Add("multileader style", () => Name(db.MLeaderstyle));
            Add("linetype", () => Name(db.Celtype));
            Add("color", () => db.Cecolor.ToString());
            Add("lineweight", () => db.Celweight.ToString());
            Add("LTSCALE", () => db.Ltscale.ToString("G6", inv));
            Add("PDMODE", () => db.Pdmode.ToString(inv));
            Add("PDSIZE", () => db.Pdsize.ToString("G6", inv));
            return h;
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
            _wouldAdd = Group(tr, added, false, books, _newHandles);
            _wouldChange = Group(tr, changed, true, books, null);
            _wouldDelete = Group(tr, erased, true, books, _deletedHandles);
            foreach (var kv in Header(_db, tr))
                if (_header0.TryGetValue(kv.Key, out var before) && before != kv.Value)
                    _settings[kv.Key] = $"{before} -> {kv.Value}";
            _bookkeeping = new JsonObject();
            foreach (var kv in books.OrderByDescending(k => k.Value)) _bookkeeping[kv.Key] = kv.Value;
        }

        static JsonObject Group(Transaction tr, List<ObjectId> ids, bool withHandle, Dictionary<string, int> books,
                                List<string> handles)
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
                handles?.Add(id.Handle.ToString());
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

        /// <summary>After a real change was committed: look again, in the saved drawing, that the new objects exist
        /// and the deleted ones are gone. Civil 3D sometimes refuses a change without saying so.</summary>
        public void Verify()
        {
            int missing = _newHandles.Count(h => !(_db.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var id) && !id.IsErased));
            int still = _deletedHandles.Count(h => _db.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var id) && !id.IsErased);
            if (missing > 0) _problems.Add($"{missing} of the new objects are not in the drawing after saving.");
            if (still > 0) _problems.Add($"{still} of the deleted objects are still in the drawing.");
        }

        /// <summary>Short account of what a committed tool really changed, for the reply ("changes").</summary>
        public JsonObject Summary()
        {
            var r = new JsonObject();
            void Types(string key, JsonObject g)
            {
                if (g != null && g["by_type"] is JsonObject t && t.Count > 0) r[key] = t.DeepClone();
            }
            Types("added", _wouldAdd);
            Types("changed", _wouldChange);
            Types("deleted", _wouldDelete);
            if (_newHandles.Count > 0)
                r["new_handles"] = new JsonArray(_newHandles.Take(5).Select(h => (JsonNode)JsonValue.Create(h)).ToArray());
            if (_settings.Count > 0) r["settings"] = _settings.DeepClone();
            if (_problems.Count > 0) r["problems"] = new JsonArray(_problems.Select(p => (JsonNode)JsonValue.Create(p)).ToArray());
            return r;
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
            if (_settings.Count > 0) r["would_change_settings"] = _settings.DeepClone();
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

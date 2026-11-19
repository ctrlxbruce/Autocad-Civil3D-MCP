using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Civil3DMcp.Handlers
{
    /// <summary>Drawing info, querying, generic properties, sysvars and command fallback.</summary>
    public static class CoreHandler
    {
        [Tool("doc.info", ReadOnly = true)]
        public static object Info(Args a, Ctx c)
        {
            var db = c.Db;
            var lt = c.Open<LayerTable>(db.LayerTableId);
            return new
            {
                name = c.Doc.Name,
                units = db.Insunits.ToString(),
                current_layer = c.Open<LayerTableRecord>(db.Clayer).Name,
                current_text_style = c.Open<TextStyleTableRecord>(db.Textstyle).Name,
                current_dim_style = c.Open<DimStyleTableRecord>(db.Dimstyle).Name,
                layer_count = lt.Cast<ObjectId>().Count(),
                extents = CivilCommonHandler.Safe(() => Conv.Plain(new Extents3d(db.Extmin, db.Extmax))),
                is_civil = SafeCivil(c)
            };
        }

        static bool SafeCivil(Ctx c) { try { return c.Civ != null; } catch { return false; } }

        [Tool("query.list", ReadOnly = true)]
        public static object QueryList(Args a, Ctx c)
        {
            string type = a.Str("type");       // e.g. "Line", "Polyline", "Circle", "TinSurface", "CogoPoint"
            string layer = a.Str("layer");
            int limit = a.Int("limit", 200);
            var items = new List<object>();
            foreach (ObjectId id in c.CurrentSpace(false))
            {
                if (items.Count >= limit) break;
                var e = c.Open(id) as Entity;
                if (e == null) continue;
                var tn = e.GetType().Name;
                if (type != null && !tn.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                    !id.ObjectClass.DxfName.Equals(type, StringComparison.OrdinalIgnoreCase)) continue;
                if (layer != null && !e.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase)) continue;
                string name = null;
                try { name = Reflect.Get(e, "Name") as string; } catch { }
                items.Add(new { handle = Ctx.H(id), type = tn, layer = e.Layer, name });
            }
            return new { count = items.Count, items };
        }

        [Tool("query.count_by_type", ReadOnly = true)]
        public static object CountByType(Args a, Ctx c) =>
            c.CurrentSpace(false).Cast<ObjectId>().GroupBy(i => i.ObjectClass.Name)
             .ToDictionary(g => g.Key, g => g.Count());

        [Tool("props.get", ReadOnly = true)]
        public static object Get(Args a, Ctx c)
        {
            var o = c.Open(a.NeedStr("handle"));
            if (a.Has("path")) return new { path = a.Str("path"), value = Conv.Plain(Reflect.GetPath(o, a.Str("path"))) };
            return Props.GetAll(o);
        }

        [Tool("props.set")]
        public static object Set(Args a, Ctx c)
        {
            var results = new Dictionary<string, object>();
            foreach (var h in a.Strs("handles").Concat(a.Has("handle") ? new[] { a.Str("handle") } : new string[0]))
            {
                var o = c.Open(h, true);
                results[h] = Props.SetMany(o, a.Obj("props"), c);
            }
            return results;
        }

        [Tool("props.methods", ReadOnly = true)]
        public static object Methods(Args a, Ctx c) => Reflect.MethodNames(c.Open(a.NeedStr("handle")).GetType());

        /// <summary>Call ANY API method on an object: e.g. surface "RaiseSurface" [2.5].</summary>
        [Tool("props.call")]
        public static object Call(Args a, Ctx c)
        {
            var o = c.Open(a.NeedStr("handle"), true);
            var args = a.Arr("args").Cast<object>().ToArray();
            return Conv.Plain(Reflect.CallCtx(c, o, a.NeedStr("method"), args));
        }

        [Tool("entity.delete")]
        public static object Delete(Args a, Ctx c)
        {
            int n = 0;
            foreach (var id in c.FindMany(a.Strs("handles"))) { c.Open(id, true).Erase(); n++; }
            return new { deleted = n };
        }

        [Tool("doc.get_sysvar", ReadOnly = true)]
        public static object GetVar(Args a, Ctx c) => new { name = a.NeedStr("name"), value = Conv.Plain(Application.GetSystemVariable(a.NeedStr("name"))) };

        [Tool("doc.set_sysvar", NoPreview = "changes a Civil 3D setting that a preview can't undo")]
        public static object SetVar(Args a, Ctx c)
        {
            var name = a.NeedStr("name");
            if (Security.IsBlockedSysvar(name))
                throw new InvalidOperationException(
                    $"Blocked for safety: {name.ToUpperInvariant()} controls which code Civil 3D trusts; change it yourself in Civil 3D (OPTIONS).");
            var cur = Application.GetSystemVariable(name);
            var val = Conv.To(a.Node("value"), cur?.GetType() ?? typeof(string), c);
            Application.SetSystemVariable(name, val);
            return new { name, value = Conv.Plain(Application.GetSystemVariable(name)) };
        }

        /// <summary>
        /// Fallback for anything without a dedicated tool. Sends text to the command line
        /// exactly as if typed. Runs after this call returns. Use "-" versions of commands
        /// (e.g. "-LAYER") to avoid dialog boxes.
        /// </summary>
        [Tool("doc.run_command", NoPreview = "sends text to the command line, which would run after the preview")]
        public static object Run(Args a, Ctx c)
        {
            var cmd = a.NeedStr("command");
            Security.CheckCommand(cmd);
            if (!cmd.EndsWith("\n") && !cmd.EndsWith(" ")) cmd += "\n";
            c.Send(cmd, true, false, true);
            return new { queued = cmd.Trim() };
        }

        [Tool("doc.zoom_extents", NoPreview = "only changes the view")]
        public static object ZoomExtents(Args a, Ctx c)
        {
            c.Send("_.ZOOM _E ", true, false, false);
            return new { ok = true };
        }

        [Tool("doc.save", NoPreview = "saves the drawing file")]
        public static object Save(Args a, Ctx c)
        {
            var path = a.Has("path") ? a.Str("path") : null;
            // The path goes to the command line inside quotes, so a quote or line break in it would start a new command.
            if (path != null && (path.IndexOfAny(new[] { '"', '\r', '\n', '\t' }) >= 0 || path.Any(char.IsControl)
                                 || !path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("path must be a .dwg file path without quotes or line breaks.");
            c.Send(path != null ? $"_.SAVEAS  \"{path}\"\n" : "_.QSAVE\n", true, false, false);
            return new { queued = true };
        }

        [Tool("doc.undo", NoPreview = "undoes earlier steps")]
        public static object Undo(Args a, Ctx c)
        {
            var (groups, tools, left) = UndoTracker.PlanUndo(c.Doc, a.Int("steps", 1));
            UndoTracker.StartUndo(c.Doc, groups);
            return new
            {
                queued = true,
                undoing = tools,
                civil3d_undo_steps = groups,
                claude_steps_left = left,
                note = "Civil 3D runs the undo right after this reply. Check the drawing (query_list or doc_summary)."
            };
        }

        [Tool("measure.distance", ReadOnly = true)]
        public static object Distance(Args a, Ctx c)
        {
            var p1 = a.Pt("p1"); var p2 = a.Pt("p2");
            var v = p2 - p1;
            return new
            {
                distance = p1.DistanceTo(p2),
                distance_2d = new Point2d(p1.X, p1.Y).GetDistanceTo(new Point2d(p2.X, p2.Y)),
                angle_deg = Math.Atan2(v.Y, v.X) * 180 / Math.PI,
                dx = v.X, dy = v.Y, dz = v.Z
            };
        }

        [Tool("measure.entity", ReadOnly = true)]
        public static object MeasureEntity(Args a, Ctx c)
        {
            var e = c.Open(a.NeedStr("handle"));
            var r = new Dictionary<string, object> { ["type"] = e.GetType().Name };
            if (e is Curve cv)
            {
                try { r["length"] = cv.GetDistanceAtParameter(cv.EndParam) - cv.GetDistanceAtParameter(cv.StartParam); } catch { }
                try { if (cv.Closed) r["area"] = cv.Area; } catch { }
                try { r["start"] = Conv.Plain(cv.StartPoint); r["end"] = Conv.Plain(cv.EndPoint); } catch { }
            }
            if (e is Hatch h) try { r["area"] = h.Area; } catch { }
            if (e is Entity en) try { r["extents"] = Conv.Plain(en.GeometricExtents); } catch { }
            return r;
        }
    }
}

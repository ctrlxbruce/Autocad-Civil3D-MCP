using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;

namespace Civil3DMcp
{
    /// <summary>Put [Tool("group.name")] on a static method (Args a, Ctx c) => object to expose it.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ToolAttribute : Attribute
    {
        public string Name { get; }
        /// <summary>Read-only tools never commit their transaction, so they cannot change the drawing.</summary>
        public bool ReadOnly { get; set; }
        /// <summary>Why the tool can't run as a preview (dry run): it acts outside the drawing's transaction, for
        /// example on files, the command line or the view. Null means it can be previewed.</summary>
        public string NoPreview { get; set; }
        public ToolAttribute(string name) { Name = name; }
    }

    internal static class Router
    {
        private static readonly Dictionary<string, (MethodInfo m, bool ro, string noPreview)> Tools =
            new(StringComparer.OrdinalIgnoreCase);
        public static int Count => Tools.Count;

        public static void Init()
        {
            Tools.Clear();
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes())
                foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = m.GetCustomAttribute<ToolAttribute>();
                    if (attr != null) Tools[attr.Name] = (m, attr.ReadOnly, attr.NoPreview);
                }
        }

        /// <summary>Runs a tool on the main thread. Every reply names the active drawing ("doc": {id, name}).
        /// expectDoc ({id, name}) is the drawing Claude last looked at; when it is given and another drawing is
        /// active, the tool is refused before it runs (see DocIds). dryRun runs the tool, reports what it would add,
        /// change and delete (see ChangeLog), then rolls everything back.</summary>
        public static JsonObject Execute(string tool, JsonObject args, JsonObject expectDoc = null, bool dryRun = false)
        {
            var reply = Run(tool, args, expectDoc, dryRun);
            try
            {
                var active = Application.DocumentManager.MdiActiveDocument;
                if (active != null) reply["doc"] = DocIds.Meta(active);
            }
            catch { }
            return reply;
        }

        private static JsonObject Run(string tool, JsonObject args, JsonObject expectDoc, bool dryRun)
        {
            if (dryRun && (tool == "system.ping" || tool == "system.list_tools"))
                return Fail($"{tool} can't be previewed; it only reads.");
            if (string.IsNullOrEmpty(tool)) return Fail("Missing 'tool'.");
            if (tool == "system.ping")
                // features: what this plugin understands, so a newer MCP server never relies on what it can't do
                // (an older plugin would ignore dry_run and run the tool for real).
                return Ok(new { pong = true, tools = Count, version = "1.2.0", features = new[] { "drawing_lock", "preview" } });
            if (tool == "system.list_tools")
                return Ok(Tools.Keys.OrderBy(k => k).ToList());

            if (!Tools.TryGetValue(tool, out var entry))
                return Fail($"Unknown tool '{tool}'. Call system.list_tools to see all.");

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return Fail("No drawing is open in Civil 3D.");
            var refusal = DocIds.Check(doc, expectDoc);
            if (refusal != null) return Fail(refusal);
            if (dryRun && entry.noPreview != null)
                return Fail($"{tool} can't be previewed: it {entry.noPreview}. Nothing was changed. "
                            + "Ask the user before running it for real.");

            string prefix = dryRun ? "Preview: the tool would fail, nothing was changed. " : "";
            // Civil 3D makes one undo step every time a tool locks the drawing, even when nothing changes (tested
            // on 2025). Reads (except C# code, which may draw and roll back) and tools that only queue a command
            // don't lock, so every Ctrl+Z undoes a real change and doc_undo can count exactly.
            bool locked = dryRun || !(UndoTracker.NoTransaction.Contains(tool) || (entry.ro && tool != "code.query"));
            if (locked)
            {
                // Civil 3D runs our jobs even while the user is in the middle of a command (tested: a LINE waiting
                // for its next point). Changing the drawing then would land inside that command and its undo step,
                // so only reads go ahead.
                string active = null;
                try { active = doc.CommandInProgress; } catch { }
                if (!string.IsNullOrEmpty(active))
                    return Fail($"Civil 3D is busy: the {active} command is running. Nothing was changed. "
                                + "Finish it or press Esc in Civil 3D, then try again.");
            }
            bool committed = false, ok = false;
            UndoTracker.BeginRun(doc);
            try
            {
                object result;
                ChangeLog log = null;
                try
                {
                    result = Invoke(doc, entry, args, tool, dryRun, locked, out log, out committed);
                }
                catch (Exception first) when (!locked && IsLockViolation(first))
                {
                    locked = true;            // this read needs a lock after all (Civil 3D wanted to update something)
                    result = Invoke(doc, entry, args, tool, dryRun, locked, out log, out committed);
                }
                ok = true;
                if (dryRun && log != null) return Ok(log.Report(result));
                try { doc.Editor.UpdateScreen(); } catch { }
                var reply = Ok(result);
                // Verify after write: what the drawing itself shows changed, checked again after the commit.
                if (log != null && committed)
                    try { log.Verify(); reply["changes"] = log.Summary(); } catch { }
                return reply;
            }
            catch (TargetInvocationException tie)
            {
                Exception ex = tie;
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                return Fail($"{prefix}{ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Fail($"{prefix}{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                UndoTracker.Finish(doc, tool, locked, committed, ok, dryRun);
            }
        }

        private static object Invoke(Document doc, (MethodInfo m, bool ro, string noPreview) entry, JsonObject args,
                                     string tool, bool dryRun, bool locked, out ChangeLog log, out bool committed)
        {
            log = null;
            committed = false;
            using (locked ? doc.LockDocument() : null)
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ctx = new Ctx(doc, tr);
                bool abort = dryRun || entry.ro || UndoTracker.NoTransaction.Contains(tool);
                // A preview reports what would change; a real change is measured the same way and verified after
                // the commit (see ChangeLog). Reads and command-only tools need neither.
                if (dryRun || !abort) log = new ChangeLog(doc.Database, tr);
                var result = entry.m.Invoke(null, new object[] { new Args(args), ctx });
                log?.Describe(tr);
                if (abort) tr.Abort();
                else { tr.Commit(); committed = true; }
                return result;
            }
        }

        private static bool IsLockViolation(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex is Autodesk.AutoCAD.Runtime.Exception ae && ae.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.LockViolation
                   || (ex?.Message ?? "").Contains("LockViolation");
        }

        private static JsonObject Ok(object result)
        {
            JsonNode node;
            try { node = JsonSerializer.SerializeToNode(result); }
            catch { node = JsonValue.Create(result?.ToString()); }
            return new JsonObject { ["ok"] = true, ["result"] = node };
        }

        private static JsonObject Fail(string msg) => new JsonObject { ["ok"] = false, ["error"] = msg };
    }
}

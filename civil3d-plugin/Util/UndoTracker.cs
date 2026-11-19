using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace Civil3DMcp
{
    /// <summary>
    /// Keeps, for each open drawing, the list of Claude's own steps on top of Civil 3D's undo history, so
    /// doc_undo can undo exactly those and never the user's work. What Civil 3D does (tested on 2025):
    ///  - every committed transaction is one undo step, even an empty one; an aborted one leaves none
    ///    (so read-only tools and tools that only queue a command abort their transaction, see Router);
    ///  - every command run from the command line is one more step (ZOOM included).
    /// Anything else that changes the drawing while no tool of ours runs (a command the user starts, Ctrl+Z,
    /// an edit in the Properties palette) empties the list: from then on Claude's earlier steps can't be reached
    /// without undoing that too. Tools whose undo effect isn't known (command line, layouts, system variables,
    /// saving) empty it as well. Only used on Civil 3D's main thread.
    /// </summary>
    internal static class UndoTracker
    {
        /// <summary>Tools that only queue a command line: their transaction is aborted, so it leaves no empty step.</summary>
        public static readonly HashSet<string> NoTransaction = new(StringComparer.OrdinalIgnoreCase)
            { "doc.run_command", "doc.zoom_extents", "doc.save", "pipes.interference_check", "doc.undo" };

        /// <summary>Tools that queue exactly one known command, which becomes one more undo step of ours.</summary>
        static readonly Dictionary<string, string> KnownCommand = new(StringComparer.OrdinalIgnoreCase)
            { ["doc.zoom_extents"] = "ZOOM" };

        /// <summary>Tools whose undo effect is unknown or not one step: they end Claude's list.</summary>
        static readonly Dictionary<string, string> Unknown = new(StringComparer.OrdinalIgnoreCase)
        {
            ["doc.run_command"] = "a command sent to the command line",
            ["doc.save"] = "saving the drawing",
            ["doc.set_sysvar"] = "a system variable change",
            ["pipes.interference_check"] = "the interference check command",
            ["layouts.create"] = "a layout change",
            ["layouts.set_current"] = "a layout change",
            ["layouts.delete"] = "a layout change",
            ["layouts.plot_pdf"] = "plotting",
        };

        /// <summary>One step in Civil 3D's undo history made by us: a tool's lock (Empty when nothing was kept, such
        /// as a read with C# code, a preview or a failed call) or a command we queued (Command, e.g. ZOOM).</summary>
        sealed class Step { public string Tool; public string Command; public bool Pending; public bool Empty; }

        static bool IsEdit(Step s) => s.Command == null && !s.Empty;

        sealed class State
        {
            public readonly List<Step> Steps = new();      // oldest first
            public string EndedBy;                         // why the list was last emptied
            public bool InRun;                             // one of our tools is running
            public int OurCommands;                        // depth of commands we queued that are running
            public int ForeignCommands;                    // depth of other commands running
            public readonly Queue<string> Expected = new(); // commands we queued, in order
            public DateTime ExpectedAt;                    // when the last one was queued
            public DateTime OursEndedAt;                   // when a command we queued last ended
            public bool AwaitStart;                        // we queued command text that hasn't started yet
            public string AwaitName;                       // the command that text starts (UNDO, ZOOM...)
            public int QueuedRunning;                      // depth of the command our text started
            public DateTime QueuedAt;
        }

        /// <summary>We just queued command-line text (SendStringToExecute) in this drawing.</summary>
        public static void Queued(Document d, string text)
        {
            var st = S(d);
            st.AwaitStart = true;
            var first = (text ?? "").TrimStart().TrimStart('\'').Split(new[] { ' ', '\n', '\r', '\t' }, 2)[0];
            st.AwaitName = Bare(first);
            st.QueuedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// True while command text we queued hasn't started or is still running in the active drawing. Civil 3D
        /// only runs queued text when its message loop turns, so the next tool call could otherwise run first and
        /// slip its own undo step in before an UNDO or ZOOM (seen in testing). MainThread waits while this is true,
        /// at most 5 s for the start and 30 s for the command itself.
        /// </summary>
        public static bool Busy()
        {
            try
            {
                var d = Application.DocumentManager.MdiActiveDocument;
                if (d == null || !States.TryGetValue(d.UnmanagedObject, out var st)) return false;
                double age = (DateTime.UtcNow - st.QueuedAt).TotalSeconds;
                if (st.AwaitStart && age < 5) return true;
                if (st.QueuedRunning > 0 && age < 30) return true;
                st.AwaitStart = false;
                st.QueuedRunning = 0;
                return false;
            }
            catch { return false; }
        }

        static readonly Dictionary<IntPtr, State> States = new();

        public static void Init()
        {
            var dm = Application.DocumentManager;
            foreach (Document d in dm) Hook(d);
            dm.DocumentCreated += (s, e) => { try { Hook(e.Document); } catch { } };
            dm.DocumentToBeDestroyed += (s, e) => { try { States.Remove(e.Document.UnmanagedObject); } catch { } };
        }

        static State S(Document d)
        {
            if (!States.TryGetValue(d.UnmanagedObject, out var st)) States[d.UnmanagedObject] = st = new State();
            return st;
        }

        static void Hook(Document d)
        {
            var st = S(d);
            d.CommandWillStart += (s, e) => OnCommandStart(st, e.GlobalCommandName);
            d.CommandEnded += (s, e) => OnCommandEnd(st, e.GlobalCommandName, true);
            d.CommandCancelled += (s, e) => OnCommandEnd(st, e.GlobalCommandName, false);
            d.CommandFailed += (s, e) => OnCommandEnd(st, e.GlobalCommandName, false);
            var db = d.Database;
            db.ObjectAppended += (s, e) => OnDbChange(st, e.DBObject);
            db.ObjectModified += (s, e) => OnDbChange(st, e.DBObject);
            db.ObjectErased += (s, e) => OnDbChange(st, e.DBObject);
        }

        static string Bare(string cmd) => (cmd ?? "").Trim().TrimStart('_', '.', '-').ToUpperInvariant();

        static void OnCommandStart(State st, string cmd)
        {
            var name = Bare(cmd);
            // Only the command our text starts opens the wait; Civil 3D runs hidden commands of its own (seen:
            // LOGINITIALWORKSPACEESW in a new session), and letting one of those end the wait let our next tool call
            // slip in before our UNDO.
            if (st.AwaitStart && name == st.AwaitName) { st.AwaitStart = false; st.QueuedRunning = 1; }
            else if (st.QueuedRunning > 0) st.QueuedRunning++;
            if (st.OurCommands > 0) { st.OurCommands++; return; }              // nested inside our command
            if (st.Expected.Count > 0 && st.Expected.Peek() == name)
            {
                st.Expected.Dequeue();
                st.OurCommands++;
                return;
            }
            st.ForeignCommands++;
            End(st, name == "U" || name == "UNDO" || name == "REDO" || name == "MREDO"
                ? "an undo or redo in Civil 3D" : $"the {name} command");
        }

        static void OnCommandEnd(State st, string cmd, bool ok)
        {
            if (st.QueuedRunning > 0) st.QueuedRunning--;
            if (st.OurCommands > 0)
            {
                st.OurCommands--;
                if (st.OurCommands == 0)
                {
                    st.OursEndedAt = DateTime.UtcNow;
                    var step = st.Steps.LastOrDefault(x => x.Pending && x.Command == Bare(cmd));
                    if (step != null)
                    {
                        step.Pending = false;
                        if (!ok) st.Steps.Remove(step);   // a cancelled command leaves no undo step
                    }
                }
                return;
            }
            if (st.ForeignCommands > 0) st.ForeignCommands--;
        }

        static void OnDbChange(State st, DBObject o)
        {
            if (st.InRun || st.OurCommands > 0 || st.ForeignCommands > 0) return;
            // Civil 3D sends some notifications just after a command (UNDO, ZOOM) has ended; those are still ours.
            if ((DateTime.UtcNow - st.OursEndedAt).TotalSeconds < 2) return;
            if (!IsDrawingContent(o)) return;
            string type = null;
            try { type = o?.GetType().Name; } catch { }
            End(st, "a change made outside a command" + (type != null ? $" (to a {type}, for example in the " +
                "Properties palette)" : " (for example in the Properties palette)"));
        }

        /// <summary>Objects a user changes without running a command: entities (Properties palette, grips) and
        /// layers or other table records (Layer palette). Everything else a user edits (Civil 3D styles, parts
        /// lists, point groups...) goes through a command, which OnCommandStart already sees. Civil 3D and the
        /// Map 3D display manager inside it update their own non-entity objects in the background, for example
        /// after an UNDO that reaches back to a new drawing's first steps (seen in testing: Map, PressurePartList,
        /// plain DBObject); those must not count as the user's work.</summary>
        static bool IsDrawingContent(DBObject o)
        {
            if (o == null) return true;
            try { return o is Entity || o is SymbolTableRecord; }
            catch { return true; }
        }

        static void End(State st, string why)
        {
            st.Steps.Clear();
            st.Expected.Clear();
            st.EndedBy = why;
        }

        static void Expect(State st, string cmd)
        {
            st.Expected.Enqueue(cmd);
            st.ExpectedAt = DateTime.UtcNow;
        }

        // ---------- called by Router (main thread) ----------

        public static void BeginRun(Document d) => S(d).InRun = true;

        /// <summary>Always called when a tool is done. locked: it locked the drawing, which is one undo step in
        /// Civil 3D whatever happened; committed: that step holds real changes; ran: it finished without error.</summary>
        public static void Finish(Document d, string tool, bool locked, bool committed, bool ran, bool preview)
        {
            var st = S(d);
            st.InRun = false;
            if (ran && !preview)
            {
                if (Unknown.TryGetValue(tool, out var why)) { End(st, why); return; }
                if (KnownCommand.TryGetValue(tool, out var cmd))
                {
                    Expect(st, cmd);
                    st.Steps.Add(new Step { Tool = tool, Command = cmd, Pending = true });
                }
            }
            if (locked) st.Steps.Add(new Step { Tool = tool, Empty = !(committed && ran && !preview) });
        }

        /// <summary>Plan undoing Claude's last `steps` drawing changes in this drawing. Returns how many Civil 3D
        /// undo steps that is and which tools; throws with a plain explanation when it isn't safe.</summary>
        public static (int groups, List<string> tools, int left) PlanUndo(Document d, int steps)
        {
            var st = S(d);
            if (steps < 1) throw new ArgumentException("steps must be 1 or more.");
            if (st.Expected.Count > 0 && (DateTime.UtcNow - st.ExpectedAt).TotalSeconds > 15)
            {
                // A command we queued never started (Civil 3D dropped it): it left no undo step.
                st.Expected.Clear();
                st.Steps.RemoveAll(x => x.Pending);
            }
            if (st.Expected.Count > 0 || st.Steps.Any(x => x.Pending))
                throw new InvalidOperationException("A command Claude started is still running in Civil 3D. Try again in a moment.");
            int edits = st.Steps.Count(IsEdit);
            if (edits == 0)
                throw new InvalidOperationException(st.EndedBy == null
                    ? "No change of Claude's is left to undo in this drawing (Claude can only undo what it did since "
                      + "Civil 3D started). Use Ctrl+Z in Civil 3D for anything else."
                    : $"Nothing of Claude's can be undone safely: after Claude's last change came {st.EndedBy}, and "
                      + "undoing past it would undo that too. Use Ctrl+Z in Civil 3D if that's what you want.");
            if (steps > edits)
                throw new InvalidOperationException($"Only {edits} of Claude's changes can be undone safely here"
                    + (st.EndedBy != null ? $" (before them came {st.EndedBy})" : "") + ".");
            int groups = 0, found = 0;
            var tools = new List<string>();
            for (int i = st.Steps.Count - 1; i >= 0 && found < steps; i--)
            {
                groups++;
                var s = st.Steps[i];
                tools.Add(s.Tool + (s.Command != null ? $" ({s.Command})" : s.Empty ? " (nothing kept)" : ""));
                if (IsEdit(s)) found++;
            }
            return (groups, tools, edits - steps);
        }

        /// <summary>Queue Civil 3D's UNDO for `groups` steps and drop them from the list.</summary>
        public static void StartUndo(Document d, int groups)
        {
            var st = S(d);
            Expect(st, "UNDO");
            st.Steps.RemoveRange(st.Steps.Count - groups, groups);
            Queued(d, "_.UNDO");
            d.SendStringToExecute($"_.UNDO {groups} ", true, false, true);
        }
    }
}

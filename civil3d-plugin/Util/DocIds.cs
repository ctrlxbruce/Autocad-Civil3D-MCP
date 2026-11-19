using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;

namespace Civil3DMcp
{
    /// <summary>
    /// Drawing lock. Every open drawing gets a random id for as long as it stays open, and every reply names the
    /// active drawing. The MCP server sends back the id of the drawing Claude last looked at (doc_info) with every
    /// tool that can change something; if the user has switched to another drawing since, the tool is refused
    /// before it runs, so Claude never edits a drawing it hasn't seen.
    /// Only used on Civil 3D's main thread.
    /// </summary>
    internal static class DocIds
    {
        // Keyed by the native document pointer; removed when the drawing closes, so a reused pointer gets a new id.
        private static readonly Dictionary<IntPtr, string> Ids = new();

        public static void Init()
        {
            Application.DocumentManager.DocumentToBeDestroyed += (s, e) =>
            {
                try { Ids.Remove(e.Document.UnmanagedObject); } catch { }
            };
        }

        public static string Of(Document d)
        {
            var key = d.UnmanagedObject;
            if (!Ids.TryGetValue(key, out var id))
                Ids[key] = id = Guid.NewGuid().ToString("N").Substring(0, 12);
            return id;
        }

        public static string Name(Document d)
        {
            try { return Path.GetFileName(d.Name); } catch { return d.Name; }
        }

        public static JsonObject Meta(Document d) => new JsonObject { ["id"] = Of(d), ["name"] = Name(d) };

        static bool IsOpen(string id)
        {
            foreach (Document d in Application.DocumentManager)
                if (Ids.TryGetValue(d.UnmanagedObject, out var v) && v == id) return true;
            return false;
        }

        /// <summary>Refusal text when the active drawing isn't the one the MCP server expects, else null.</summary>
        public static string Check(Document active, JsonObject expect)
        {
            var want = expect?["id"]?.ToString();
            if (string.IsNullOrEmpty(want) || want == Of(active)) return null;
            var wantName = expect["name"]?.ToString();
            if (string.IsNullOrEmpty(wantName)) wantName = "another drawing";
            var where = IsOpen(want) ? "still open in another tab" : "no longer open";
            return $"Drawing lock: nothing was changed. The active drawing is now '{Name(active)}', but you last looked "
                 + $"at '{wantName}' ({where}). Ask the user which drawing to work on. To work on '{Name(active)}', "
                 + "call doc_info and then try again.";
        }
    }
}

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
        public ToolAttribute(string name) { Name = name; }
    }

    internal static class Router
    {
        private static readonly Dictionary<string, (MethodInfo m, bool ro)> Tools = new(StringComparer.OrdinalIgnoreCase);
        public static int Count => Tools.Count;

        public static void Init()
        {
            Tools.Clear();
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes())
                foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = m.GetCustomAttribute<ToolAttribute>();
                    if (attr != null) Tools[attr.Name] = (m, attr.ReadOnly);
                }
        }

        public static JsonObject Execute(string tool, JsonObject args)
        {
            if (string.IsNullOrEmpty(tool)) return Fail("Missing 'tool'.");
            if (tool == "system.ping")
                return Ok(new { pong = true, tools = Count, version = "1.0.0" });
            if (tool == "system.list_tools")
                return Ok(Tools.Keys.OrderBy(k => k).ToList());

            if (!Tools.TryGetValue(tool, out var entry))
                return Fail($"Unknown tool '{tool}'. Call system.list_tools to see all.");

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return Fail("No drawing is open in Civil 3D.");

            try
            {
                object result;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ctx = new Ctx(doc, tr);
                    result = entry.m.Invoke(null, new object[] { new Args(args), ctx });
                    if (entry.ro) tr.Abort(); else tr.Commit();
                }
                try { doc.Editor.UpdateScreen(); } catch { }
                return Ok(result);
            }
            catch (TargetInvocationException tie)
            {
                Exception ex = tie;
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                return Fail($"{ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return Fail($"{ex.GetType().Name}: {ex.Message}");
            }
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

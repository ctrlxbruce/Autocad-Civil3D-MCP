using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(Civil3DMcp.SelfTest))]

namespace Civil3DMcp
{
    /// <summary>
    /// MCPTEST: runs mcp-tests.json (next to the DLL) through the real TCP port, exactly like the
    /// MCP server would, and writes mcp-results.json. Steps: {"tool":"...","args":{...},"save":"name"}.
    /// Any string arg like "$name.handle" or "$name.handles[0]" is replaced with a saved result value.
    /// </summary>
    public class SelfTest
    {
        [CommandMethod("MCPTEST")]
        public void Run()
        {
            var dir = Path.GetDirectoryName(typeof(SelfTest).Assembly.Location);
            var input = Path.Combine(dir, "mcp-tests.json");
            var output = Path.Combine(dir, "mcp-results.json");
            if (!File.Exists(input)) { PluginEntry.Say($"\n[MCPTEST] {input} not found.\n"); return; }
            PluginEntry.Say($"\n[MCPTEST] Running {input} in the background. Results -> {output}\n");
            new Thread(() => Worker(input, output)) { IsBackground = true }.Start();
        }

        static void Worker(string input, string output)
        {
            var steps = JsonNode.Parse(File.ReadAllText(input)).AsArray();
            var saved = new Dictionary<string, JsonNode>();
            var results = new JsonArray();
            int ok = 0, fail = 0;
            try
            {
                using var client = new TcpClient("127.0.0.1", PluginEntry.Port);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false));
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                int id = 0;
                foreach (var stepNode in steps)
                {
                    var step = stepNode.AsObject();
                    var tool = step["tool"]?.ToString();
                    var args = Substitute(step["args"]?.DeepClone() ?? new JsonObject(), saved);
                    var sw = Stopwatch.StartNew();
                    writer.WriteLine(new JsonObject { ["id"] = ++id, ["token"] = Security.Token, ["tool"] = tool, ["args"] = args }.ToJsonString());
                    var replyText = reader.ReadLine();
                    sw.Stop();
                    var reply = JsonNode.Parse(replyText).AsObject();
                    bool good = reply["ok"]?.GetValue<bool>() == true;
                    if (good) ok++; else fail++;
                    if (good && step["save"] != null) saved[step["save"].ToString()] = reply["result"]?.DeepClone();
                    var resText = reply["result"]?.ToJsonString() ?? "";
                    results.Add(new JsonObject
                    {
                        ["n"] = id, ["tool"] = tool, ["ok"] = good, ["ms"] = sw.ElapsedMilliseconds,
                        ["error"] = reply["error"]?.ToString(),
                        ["result"] = resText.Length > 1500 ? JsonValue.Create(resText.Substring(0, 1500) + "...") : reply["result"]?.DeepClone()
                    });
                    File.WriteAllText(output, new JsonObject { ["done"] = false, ["ok"] = ok, ["fail"] = fail, ["steps"] = results.DeepClone() }
                        .ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (System.Exception ex)
            {
                results.Add(new JsonObject { ["fatal"] = ex.ToString() });
            }
            File.WriteAllText(output, new JsonObject { ["done"] = true, ["ok"] = ok, ["fail"] = fail, ["steps"] = results }
                .ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        static JsonNode Substitute(JsonNode n, Dictionary<string, JsonNode> saved)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var k in o.Select(kv => kv.Key).ToList()) o[k] = Substitute(o[k]?.DeepClone(), saved);
                    return o;
                case JsonArray a:
                    var na = new JsonArray();
                    foreach (var x in a) na.Add(Substitute(x?.DeepClone(), saved));
                    return na;
                case JsonValue v when v.TryGetValue<string>(out var s) && s.StartsWith("$"):
                    return Lookup(s.Substring(1), saved)?.DeepClone() ?? v;
                default:
                    return n;
            }
        }

        static JsonNode Lookup(string path, Dictionary<string, JsonNode> saved)
        {
            var parts = path.Split('.');
            if (!saved.TryGetValue(parts[0], out var cur)) return null;
            foreach (var raw in parts.Skip(1))
            {
                var p = raw; int? idx = null;
                var b = p.IndexOf('[');
                if (b >= 0) { idx = int.Parse(p.Substring(b + 1, p.IndexOf(']') - b - 1)); p = p.Substring(0, b); }
                if (p.Length > 0) cur = cur?[p];
                if (idx.HasValue) cur = cur?[idx.Value];
            }
            return cur;
        }
    }
}

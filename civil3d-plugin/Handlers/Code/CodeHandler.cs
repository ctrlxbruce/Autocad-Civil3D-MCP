using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Civil3DMcp.Handlers
{
    /// <summary>Values available inside code.execute / code.query scripts.</summary>
    public class ScriptGlobals
    {
        public Document Document;
        public CivilDocument CivilDoc;
        public Database Database;
        public Transaction Transaction;
        public Editor Editor;
        public Ctx Ctx;
        public Dictionary<string, object> Inputs = new();
        public List<string> Log = new();
        public void Print(object o) => Log.Add(o?.ToString());
    }

    /// <summary>
    /// Runs C# written by Claude INSIDE Civil 3D with the full AutoCAD + Civil 3D .NET API.
    /// Uses the Roslyn compiler that ships with AutoCAD (no extra DLLs).
    /// The script body becomes a method; globals are local variables; `return x;` sends x back as JSON.
    /// code.execute commits; code.query always rolls back.
    /// </summary>
    public static class CodeHandler
    {
        static List<MetadataReference> _refs;

        static readonly (Regex rx, string why)[] Blocked =
        {
            (new Regex(@"\bProcess\s*\.\s*Start|System\.Diagnostics\.Process\b"), "starting programs"),
            (new Regex(@"\b(File|Directory)\s*\.\s*(Delete|Move)|\bFileInfo\b.*\.Delete"), "deleting/moving files"),
            (new Regex(@"\b(HttpClient|WebClient|WebRequest|TcpClient|UdpClient|Socket)\b"), "network access"),
            (new Regex(@"\bRegistry(Key)?\b"), "registry access"),
            (new Regex(@"\bAssembly\s*\.\s*(Load|LoadFrom|LoadFile|UnsafeLoadFrom)\b|AssemblyLoadContext"), "loading assemblies"),
            (new Regex(@"\bEnvironment\s*\.\s*(Exit|FailFast)\b|Application\s*\.\s*Quit\b"), "closing Civil 3D"),
            (new Regex(@"\bMarshal\s*\.|\bunsafe\b|DllImport"), "unsafe/native code"),
            (new Regex(@"(?i)\b(" + string.Join("|", Security.BlockedSysvarNames) + @")\b"), "changing Civil 3D's code-loading security"),
        };

        const string Usings = @"using System; using System.Linq; using System.Collections.Generic; using System.Text;
using Autodesk.AutoCAD.ApplicationServices; using Autodesk.AutoCAD.DatabaseServices; using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput; using Autodesk.AutoCAD.Colors;
using Autodesk.Civil; using Autodesk.Civil.ApplicationServices; using Autodesk.Civil.DatabaseServices; using Autodesk.Civil.DatabaseServices.Styles; using Civil3DMcp;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Surface = Autodesk.Civil.DatabaseServices.Surface;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
";

        static int _refsAssemblyCount;

        static List<MetadataReference> Refs()
        {
            // Civil 3D objects (Pipe, Structure, ...) derive from AEC base classes in AecBaseMgd, which Civil 3D
            // loads lazily. Walking the base types loads those assemblies before references are collected.
            for (var t = typeof(Autodesk.Civil.DatabaseServices.Entity); t != null; t = t.BaseType) { _ = t.Assembly; }
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .ToList();
            // Rebuild when Civil 3D has loaded more assemblies since the last script.
            if (_refs != null && assemblies.Count == _refsAssemblyCount) return _refs;
            _refsAssemblyCount = assemblies.Count;
            _refs = assemblies
                .GroupBy(a => a.GetName().Name).Select(g => g.First())
                .Select(a => { try { return (MetadataReference)MetadataReference.CreateFromFile(a.Location); } catch { return null; } })
                .Where(r => r != null).ToList();
            return _refs;
        }

        static string Wrap(string code, out int headerLines)
        {
            // Lift the user's own `using` lines to the top of the file.
            var lines = code.Replace("\r\n", "\n").Split('\n').ToList();
            var userUsings = new StringBuilder();
            while (lines.Count > 0 && (lines[0].TrimStart().StartsWith("using ") && lines[0].TrimEnd().EndsWith(";") || string.IsNullOrWhiteSpace(lines[0])))
            {
                userUsings.AppendLine(lines[0]);
                lines.RemoveAt(0);
            }
            var header = Usings + userUsings +
@"public static class __Script {
  public static object __Run(Civil3DMcp.Handlers.ScriptGlobals __g) {
    var Document = __g.Document; var CivilDoc = __g.CivilDoc; var Database = __g.Database;
    var Transaction = __g.Transaction; var Editor = __g.Editor; var Ctx = __g.Ctx; var Inputs = __g.Inputs;
    void Print(object o) => __g.Print(o);
#line 1
";
            headerLines = header.Count(ch => ch == '\n');
            return header + string.Join("\n", lines) + "\n#line hidden\n    return null;\n  }\n}\n";
        }

        static object Run(Args a, Ctx c)
        {
            var code = a.NeedStr("code");
            foreach (var (rx, why) in Blocked)
                if (rx.IsMatch(code)) throw new InvalidOperationException($"Blocked for safety: code tries {why}.");

            var source = Wrap(code, out _);
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var comp = CSharpCompilation.Create("C3DScript_" + Guid.NewGuid().ToString("N"), new[] { tree }, Refs(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                                             allowUnsafe: false, nullableContextOptions: NullableContextOptions.Disable));
            using var ms = new MemoryStream();
            var emit = comp.Emit(ms);
            if (!emit.Success)
            {
                var errs = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(15)
                    .Select(d => { var l = d.Location.GetMappedLineSpan(); return $"line {l.StartLinePosition.Line + 1}: {d.Id} {d.GetMessage()}"; });
                throw new ArgumentException("C# compile errors:\n" + string.Join("\n", errs));
            }
            ScriptGuard.Check(comp, tree);
            ms.Position = 0;
            var alc = new AssemblyLoadContext("C3DScript", isCollectible: true);
            try
            {
                var asm = alc.LoadFromStream(ms);
                var run = asm.GetType("__Script").GetMethod("__Run");
                var g = new ScriptGlobals
                {
                    Document = c.Doc, Database = c.Db, Transaction = c.Tr, Editor = c.Ed, Ctx = c,
                    CivilDoc = SafeCivil(c)
                };
                var inputs = a.Obj("inputs");
                if (inputs != null) foreach (var kv in inputs) g.Inputs[kv.Key] = Conv.Unwrap(kv.Value);
                object ret;
                try { ret = run.Invoke(null, new object[] { g }); }
                catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
                return new { result = Shape(ret), log = g.Log };
            }
            finally { alc.Unload(); }
        }

        static CivilDocument SafeCivil(Ctx c) { try { return c.Civ; } catch { return null; } }

        /// <summary>Plain data stays as-is (copied to plain dictionaries); AutoCAD/Civil objects become handles/property maps.</summary>
        static object Shape(object v, int depth = 0)
        {
            if (v == null) return null;
            var t = v.GetType();
            if (t.IsPrimitive || v is string || v is decimal) return v;
            if (v is Enum) return v.ToString();
            if (depth > 6) return v.ToString();
            if (t.Name.Contains("AnonymousType") || t.Name.StartsWith("ValueTuple"))
            {
                var d = new Dictionary<string, object>();
                foreach (var p in t.GetProperties()) d[p.Name] = Shape(p.GetValue(v), depth + 1);
                foreach (var f in t.GetFields()) d[f.Name] = Shape(f.GetValue(v), depth + 1);
                return d;
            }
            if (v is IDictionary dict)
            {
                var o = new Dictionary<string, object>();
                foreach (DictionaryEntry e in dict) o[e.Key.ToString()] = Shape(e.Value, depth + 1);
                return o;
            }
            if (v is IEnumerable en && !(t.Namespace ?? "").StartsWith("Autodesk"))
            {
                var list = new List<object>();
                foreach (var x in en) { list.Add(Shape(x, depth + 1)); if (list.Count >= 2000) break; }
                return list;
            }
            return Conv.Plain(v);
        }

        [Tool("code.execute")]
        public static object Execute(Args a, Ctx c) => Run(a, c);

        [Tool("code.query", ReadOnly = true)]
        public static object Query(Args a, Ctx c) => Run(a, c);
    }
}

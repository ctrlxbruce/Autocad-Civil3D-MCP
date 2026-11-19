using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Civil3DMcp.Handlers
{
    /// <summary>
    /// Refuses scripts that reach outside the drawing: start programs, write or delete files, use the network or
    /// the registry, load code, call methods through reflection, use COM / dynamic, or send text to the command
    /// line (which would get around doc.run_command's checks).
    ///
    /// It checks what the compiler resolved, not the script text, so `using P = System.Diagnostics.Process;`,
    /// `new Process()` after `using System.Diagnostics;` and `var x = File.WriteAllText` are all caught, while
    /// a name that only looks similar (a Civil 3D `StartStation`) is not. This is a second line of defence after
    /// the token and Claude's own permission prompts, not a sandbox.
    /// </summary>
    static class ScriptGuard
    {
        // Every member of these types.
        static readonly HashSet<string> BlockedTypes = new()
        {
            "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo", "System.Activator",
            "System.IO.FileStream", "System.Runtime.Loader.AssemblyLoadContext", "Microsoft.VisualBasic.Interaction",
            "Autodesk.AutoCAD.Runtime.DynamicLinker", "Civil3DMcp.Reflect", "Civil3DMcp.Router", "Civil3DMcp.Listener",
            "Civil3DMcp.Security", "Civil3DMcp.PluginEntry",
        };

        // Every type in these namespaces (and below).
        static readonly string[] BlockedNamespaces =
        {
            "System.Net", "Microsoft.Win32", "System.Runtime.InteropServices", "System.Reflection.Emit",
            "System.Linq.Expressions", "System.IO.Pipes", "System.Management", "System.CodeDom",
        };

        // Named members of these types.
        static readonly Dictionary<string, string[]> BlockedMembers = new()
        {
            ["System.Type"] = new[] { "GetTypeFromProgID", "GetTypeFromCLSID", "InvokeMember" },
            ["System.Reflection.MethodBase"] = new[] { "Invoke" },
            ["System.Reflection.MethodInfo"] = new[] { "Invoke", "CreateDelegate" },
            ["System.Reflection.ConstructorInfo"] = new[] { "Invoke" },
            ["System.Reflection.Assembly"] = new[] { "Load", "LoadFrom", "LoadFile", "UnsafeLoadFrom", "LoadWithPartialName", "CreateInstance" },
            ["System.Delegate"] = new[] { "CreateDelegate", "DynamicInvoke" },
            ["System.AppDomain"] = new[] { "Load", "ExecuteAssembly", "ExecuteAssemblyByName", "CreateInstance", "CreateInstanceAndUnwrap", "CreateInstanceFrom", "CreateInstanceFromAndUnwrap", "CreateDomain" },
            ["System.Environment"] = new[] { "Exit", "FailFast", "SetEnvironmentVariable" },
            ["Autodesk.AutoCAD.ApplicationServices.Document"] = new[] { "SendStringToExecute", "AcadDocument" },
            ["Autodesk.AutoCAD.ApplicationServices.Application"] = new[] { "Invoke", "AcadApplication", "Quit" },
            ["Autodesk.AutoCAD.ApplicationServices.Core.Application"] = new[] { "Invoke", "AcadApplication", "Quit" },
            ["Autodesk.AutoCAD.ApplicationServices.DocumentExtension"] = new[] { "GetAcadDocument" },
            ["Autodesk.AutoCAD.EditorInput.Editor"] = new[] { "Command", "CommandAsync" },
        };

        // File-system types where reading is fine but writing, copying, moving or deleting is not.
        static readonly HashSet<string> FileTypes = new()
        {
            "System.IO.File", "System.IO.FileInfo", "System.IO.Directory", "System.IO.DirectoryInfo", "System.IO.FileSystemInfo",
        };
        static readonly string[] FileWritePrefixes =
            { "Write", "Append", "Copy", "Move", "Delete", "Replace", "Create", "Set", "Encrypt", "Decrypt", "Open" };
        static readonly HashSet<string> FileReadOpeners = new() { "OpenRead", "OpenText" };

        public static void Check(CSharpCompilation comp, SyntaxTree tree)
        {
            var model = comp.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is not (SimpleNameSyntax or BaseObjectCreationExpressionSyntax)) continue;
                if (node is SimpleNameSyntax && model.GetTypeInfo(node).Type?.TypeKind == TypeKind.Dynamic)
                    Refuse(node, "`dynamic` (late-bound COM calls)");
                var info = model.GetSymbolInfo(node);
                var sym = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                if (sym == null) continue;
                var why = Reason(sym, node);
                if (why != null) Refuse(node, why);
            }
        }

        static string Reason(ISymbol sym, SyntaxNode node)
        {
            if (sym is IAliasSymbol alias) sym = alias.Target;
            if (sym is INamespaceSymbol ns) return InBlockedNamespace(ns.ToDisplayString()) ? ns.ToDisplayString() : null;
            if (sym is IDynamicTypeSymbol) return "`dynamic` (late-bound COM calls)";

            var type = (sym as ITypeSymbol ?? sym.ContainingType)?.OriginalDefinition;
            if (type == null) return null;
            var typeName = type.ToDisplayString();
            var nsName = type.ContainingNamespace?.ToDisplayString() ?? "";

            if (BlockedTypes.Contains(typeName)) return typeName;
            if (InBlockedNamespace(nsName)) return typeName;
            if (sym is ITypeSymbol) return null;          // naming an allowed type is fine; its members are checked below

            var member = sym.Name;
            for (var t = type; t != null; t = t.BaseType?.OriginalDefinition)
                if (BlockedMembers.TryGetValue(t.ToDisplayString(), out var names) && names.Contains(member))
                    return $"{t.ToDisplayString()}.{member}";

            if (FileTypes.Contains(typeName) && !FileReadOpeners.Contains(member) &&
                FileWritePrefixes.Any(p => member.StartsWith(p, StringComparison.Ordinal)))
                return $"{typeName}.{member} (writing, copying, moving or deleting files)";
            if (typeName == "System.IO.FileInfo" && member is "MoveTo" or "CopyTo")
                return $"{typeName}.{member} (copying or moving files)";
            if (typeName == "System.IO.StreamWriter" && sym is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor &&
                ctor.Parameters.FirstOrDefault()?.Type.SpecialType == SpecialType.System_String)
                return "System.IO.StreamWriter(path) (writing files)";
            return null;
        }

        static bool InBlockedNamespace(string ns) =>
            BlockedNamespaces.Any(b => ns == b || ns.StartsWith(b + ".", StringComparison.Ordinal));

        static void Refuse(SyntaxNode node, string what)
        {
            var line = node.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1;
            throw new InvalidOperationException(
                $"Blocked for safety (line {line}): scripts can't use {what}. Scripts may read and change the drawing "
                + "and read files, but not start programs, write or delete files, use the network or registry, load code, "
                + "or send commands. Return the data instead (for files, use the dedicated tools such as qty_cost_estimate).");
        }
    }
}

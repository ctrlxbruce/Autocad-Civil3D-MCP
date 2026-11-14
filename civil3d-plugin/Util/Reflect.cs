using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Civil.ApplicationServices;

namespace Civil3DMcp
{
    /// <summary>
    /// Calls the Civil 3D API by name. Used for Civil objects so the plugin compiles on
    /// every Civil 3D version; if a method/property is missing you get a clear error
    /// listing what IS available, instead of a build failure.
    /// </summary>
    public static class Reflect
    {
        const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        const BindingFlags Stat = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;

        static readonly string[] Namespaces =
        {
            "Autodesk.Civil.DatabaseServices.", "Autodesk.Civil.DatabaseServices.Styles.",
            "Autodesk.Civil.", "Autodesk.Civil.ApplicationServices.", "Autodesk.Civil.Settings."
        };

        /// <summary>Find a Civil 3D type by short name, e.g. T("TinSurface").</summary>
        public static Type T(string name)
        {
            var asm = typeof(CivilDocument).Assembly;
            foreach (var ns in Namespaces)
            {
                var t = asm.GetType(ns + name, false, true);
                if (t != null) return t;
            }
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(x => x.GetName().Name.StartsWith("Aecc")))
                foreach (var ns in Namespaces)
                {
                    var t = a.GetType(ns + name, false, true);
                    if (t != null) return t;
                }
            throw new TypeLoadException($"Civil 3D type '{name}' not found.");
        }

        public static object Enum(string typeName, string value) => System.Enum.Parse(T(typeName), value, true);

        /// <summary>Case-insensitive property lookup that copes with properties hidden by "new" in derived classes.</summary>
        public static PropertyInfo Prop(Type t, string name, bool forWrite = false)
        {
            PropertyInfo fallback = null;
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                foreach (var p in cur.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || p.GetIndexParameters().Length != 0) continue;
                    // Civil 3D wrappers sometimes redeclare a property with only a getter or only a setter.
                    if (forWrite ? p.CanWrite : p.CanRead) return p;
                    fallback ??= p;
                }
            }
            if (fallback != null) return fallback;
            throw new MissingMemberException($"{t.Name} has no property '{name}'. Has: {Names(t)}");
        }

        // ---------- properties ----------
        public static object Get(object o, string name)
        {
            if (o == null) throw new ArgumentNullException(nameof(o), $"Cannot read '{name}' of null.");
            return Prop(o.GetType(), name).GetValue(o);
        }

        public static object GetPath(object o, string path)
        {
            foreach (var part in path.Split('.')) o = Get(o, part);
            return o;
        }

        public static void Set(object o, string name, object value, Ctx c = null)
        {
            var p = Prop(o.GetType(), name, forWrite: true);
            if (!p.CanWrite) throw new InvalidOperationException($"{o.GetType().Name}.{p.Name} is read-only.");
            p.SetValue(o, Conv.To(value, p.PropertyType, c));
        }

        /// <summary>Set a dotted path like "ContourStyle.MajorContourInterval".</summary>
        public static void SetPath(object o, string path, object value, Ctx c = null)
        {
            var parts = path.Split('.');
            for (int i = 0; i < parts.Length - 1; i++) o = Get(o, parts[i]);
            Set(o, parts[^1], value, c);
        }

        /// <summary>Indexer: obj[i] or obj["name"].</summary>
        public static object Index(object o, object key)
        {
            var raw = Conv.Unwrap(key);
            var idx = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 1)
                .OrderByDescending(p =>
                {
                    var t = p.GetIndexParameters()[0].ParameterType;
                    if (raw is double || raw is int) return t == typeof(int) ? 2 : 0;
                    return t == typeof(string) ? 2 : 0;
                }).ToList();
            Exception last = null;
            foreach (var p in idx)
            {
                object k;
                try { k = Conv.To(raw, p.GetIndexParameters()[0].ParameterType, null); } catch { continue; }
                try { return p.GetValue(o, new[] { k }); }
                catch (TargetInvocationException tie) { last = tie.InnerException ?? tie; }
            }
            if (last != null) throw last;
            throw new MissingMemberException($"{o.GetType().Name} has no indexer for '{key}'.");
        }

        // ---------- methods ----------
        public static object Call(object o, string method, params object[] args) =>
            Invoke(o.GetType(), o, method, args, out _, null);

        public static object CallCtx(Ctx c, object o, string method, params object[] args) =>
            Invoke(o.GetType(), o, method, args, out _, c);

        public static object CallOut(object o, string method, out object[] finalArgs, params object[] args) =>
            Invoke(o.GetType(), o, method, args, out finalArgs, null);

        public static object CallStatic(Type t, string method, params object[] args) =>
            Invoke(t, null, method, args, out _, null);

        public static object CallStaticOut(Type t, string method, out object[] finalArgs, params object[] args) =>
            Invoke(t, null, method, args, out finalArgs, null);

        static object Invoke(Type t, object target, string method, object[] args, out object[] finalArgs, Ctx c)
        {
            args ??= Array.Empty<object>();
            var flags = target == null ? Stat : Inst;
            var candidates = t.GetMethods(flags)
                .Where(m => m.Name.Equals(method, StringComparison.OrdinalIgnoreCase) && m.GetParameters().Length == args.Length)
                .Select(m => new { m, score = Score(m, args) })
                .OrderByDescending(x => x.score)
                .ToList();

            foreach (var cand in candidates)
            {
                var ps = cand.m.GetParameters();
                var conv = new object[args.Length];
                bool ok = true;
                for (int i = 0; i < args.Length && ok; i++)
                {
                    try { conv[i] = Conv.To(args[i], ps[i].ParameterType, c); }
                    catch { ok = false; }
                }
                if (!ok) continue;
                object result;
                try { result = cand.m.Invoke(target, conv); }
                catch (TargetInvocationException tie) when (tie.InnerException != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                    throw;
                }
                finalArgs = conv;
                return result;
            }

            var sigs = t.GetMethods(flags).Where(m => m.Name.Equals(method, StringComparison.OrdinalIgnoreCase))
                .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})")
                .ToList();
            throw new MissingMethodException(sigs.Count == 0
                ? $"{t.Name} has no method '{method}'."
                : $"No overload of {t.Name}.{method} fits {args.Length} args. Available: {string.Join(" | ", sigs)}");
        }

        static int Score(MethodInfo m, object[] args)
        {
            var ps = m.GetParameters();
            int s = 0;
            for (int i = 0; i < args.Length; i++)
            {
                var pt = ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType() : ps[i].ParameterType;
                if (args[i] != null && pt.IsInstanceOfType(args[i])) s += 2;
                else if (args[i] is string && pt.IsEnum) s += 1;
            }
            return s;
        }

        public static string Names(Type t) =>
            string.Join(", ", t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Distinct().OrderBy(x => x));

        public static List<string> MethodNames(Type t) =>
            t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
             .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))
             .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})")
             .Distinct().OrderBy(x => x).ToList();
    }
}

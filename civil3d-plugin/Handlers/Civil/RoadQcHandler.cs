using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Civil3DMcp.Handlers
{
    /// <summary>
    /// Road geometry checks against a design speed: stopping sight distance, vertical curve K values and
    /// horizontal curve radii. standard "aashto" (default) or "irc". Values are indicative; confirm with your code.
    /// </summary>
    public static class RoadQcHandler
    {
        static string Standard(Args a)
        {
            var s = a.Str("standard", "aashto").ToLowerInvariant();
            if (s != "aashto" && s != "irc") throw new ArgumentException("standard must be aashto or irc.");
            return s;
        }

        static IEnumerable<object> Items(object collection)
        {
            if (collection is IEnumerable e) foreach (var x in e) if (x != null) yield return x;
        }

        static double? D(object o, string name)
        {
            try { var v = Reflect.Get(o, name); return v == null ? (double?)null : Convert.ToDouble(v); } catch { return null; }
        }

        [Tool("qc.stopping_distance", ReadOnly = true)]
        public static object StoppingDistance(Args a, Ctx c)
        {
            var std = Standard(a);
            double v = a.Dbl("design_speed"), g = a.Dbl("grade", 0) / 100.0, t = a.Dbl("reaction_time", 2.5);
            if (v <= 0) throw new ArgumentException("design_speed (km/h) must be positive.");
            double ssd = Hydraulics.StoppingDistance(v, g, std, t);
            return new
            {
                design_speed_kmh = v, grade_percent = g * 100, standard = std, reaction_time_s = t,
                stopping_sight_distance_m = Math.Round(ssd, 1),
                min_k_crest = Math.Round(Hydraulics.MinK(ssd, true, std), 1),
                min_k_sag = Math.Round(Hydraulics.MinK(ssd, false, std), 1),
                formula = std == "irc" ? "SSD = 0.278 V t + V² / (254 (f + G)), f from IRC:SP:23" : "SSD = 0.278 V t + V² / (254 (a/g + G)), a = 3.4 m/s²",
            };
        }

        [Tool("qc.profile_k_values", ReadOnly = true)]
        public static object ProfileKValues(Args a, Ctx c)
        {
            var std = Standard(a);
            double v = a.Dbl("design_speed");
            if (v <= 0) throw new ArgumentException("design_speed (km/h) must be positive.");
            double f = Hydraulics.ToMeters(c.Db);
            var profile = c.Open(a.NeedStr("profile"));
            double ssd = Hydraulics.StoppingDistance(v, 0, std, a.Dbl("reaction_time", 2.5));
            double kCrest = Hydraulics.MinK(ssd, true, std), kSag = Hydraulics.MinK(ssd, false, std);

            var curves = new List<object>();
            int problems = 0;
            foreach (var ent in Items(Reflect.Get(profile, "Entities")))
            {
                var type = CivilCommonHandler.Safe(() => Reflect.Get(ent, "EntityType")?.ToString()) ?? ent.GetType().Name;
                if (type.IndexOf("Tangent", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                double? gin = D(ent, "GradeIn"), gout = D(ent, "GradeOut"), len = D(ent, "Length");
                if (gin == null || gout == null || len == null || len <= 0) continue;
                double aPct = Math.Abs(gout.Value - gin.Value) * 100;
                if (aPct < 1e-9) continue;
                bool crest = gin > gout;
                double k = len.Value * f / aPct, kMin = crest ? kCrest : kSag;
                bool ok = k + 1e-9 >= kMin;
                if (!ok) problems++;
                curves.Add(new
                {
                    start_station = D(ent, "StartStation"), end_station = D(ent, "EndStation"), type = crest ? "crest" : "sag",
                    length_m = Math.Round(len.Value * f, 2), grade_in_percent = Math.Round(gin.Value * 100, 3), grade_out_percent = Math.Round(gout.Value * 100, 3),
                    k = Math.Round(k, 1), k_min = Math.Round(kMin, 1), ok,
                    fix = ok ? null : $"lengthen to at least {Math.Round(kMin * aPct, 1)} m",
                });
            }
            return new
            {
                profile = Reflect.Get(profile, "Name"), design_speed_kmh = v, standard = std,
                stopping_sight_distance_m = Math.Round(ssd, 1), k_min_crest = Math.Round(kCrest, 1), k_min_sag = Math.Round(kSag, 1),
                curves, curves_below_minimum = problems,
                note = curves.Count == 0 ? "No vertical curves found in this profile." : "K = L / A (m per % grade change). Minimums from stopping sight distance on level grade.",
            };
        }

        [Tool("qc.alignment", ReadOnly = true)]
        public static object AlignmentCheck(Args a, Ctx c)
        {
            var std = Standard(a);
            double v = a.Dbl("design_speed");
            if (v <= 0) throw new ArgumentException("design_speed (km/h) must be positive.");
            double f = Hydraulics.ToMeters(c.Db);
            double e = a.Dbl("max_superelevation", 0.07);
            double side = a.Has("side_friction") ? a.Dbl("side_friction") : (std == "irc" ? 0.15 : Hydraulics.AashtoSideFriction(v));
            double rMin = v * v / (127 * (e + side));
            var al = c.Open(a.NeedStr("alignment"));

            var curves = new List<object>();
            int problems = 0;
            void AddCurve(object src, double radius, string kind)
            {
                double r = radius * f;
                bool ok = r + 1e-9 >= rMin;
                if (!ok) problems++;
                curves.Add(new { kind, start_station = D(src, "StartStation"), end_station = D(src, "EndStation"),
                                 radius_m = Math.Round(r, 2), length_m = D(src, "Length") is double l ? Math.Round(l * f, 2) : (double?)null, ok });
            }
            foreach (var ent in Items(Reflect.Get(al, "Entities")))
            {
                var r = D(ent, "Radius");
                if (r.HasValue && r.Value > 0) { AddCurve(ent, r.Value, "arc"); continue; }
                int subs = (int)(D(ent, "SubEntityCount") ?? 0);
                for (int i = 0; i < subs; i++)
                {
                    object sub = CivilCommonHandler.Safe(() => Reflect.Index(ent, i));
                    var sr = sub == null ? null : D(sub, "Radius");
                    if (sr.HasValue && sr.Value > 0 && sr.Value < 1e8) AddCurve(sub, sr.Value, "arc (in spiral group)");
                }
            }
            return new
            {
                alignment = Reflect.Get(al, "Name"), design_speed_kmh = v, standard = std, max_superelevation = e, side_friction = Math.Round(side, 3),
                min_radius_m = Math.Round(rMin, 1), curves, curves_below_minimum = problems,
                formula = "R min = V² / (127 (e + f))",
            };
        }
    }
}

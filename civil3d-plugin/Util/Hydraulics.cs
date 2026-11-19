using System;
using Autodesk.AutoCAD.DatabaseServices;

namespace Civil3DMcp
{
    /// <summary>
    /// Pipe-flow and road-design formulas used by the hydraulics, hydrology and QC tools.
    /// All inputs and outputs are SI (m, m/s, m³/s) unless a name says otherwise.
    /// </summary>
    public static class Hydraulics
    {
        /// <summary>Factor that converts drawing units to metres (from INSUNITS).</summary>
        public static double ToMeters(Database db)
        {
            switch (db.Insunits)
            {
                case UnitsValue.Millimeters: return 0.001;
                case UnitsValue.Centimeters: return 0.01;
                case UnitsValue.Kilometers: return 1000.0;
                case UnitsValue.Inches: return 0.0254;
                case UnitsValue.Feet: return 0.3048;
                case UnitsValue.Yards: return 0.9144;
                default: return 1.0;
            }
        }

        /// <summary>Full-flow capacity (m³/s) and velocity (m/s) of a circular pipe, Manning's equation.</summary>
        public static (double q, double v) FullFlow(double diameter, double slope, double n)
        {
            if (diameter <= 0 || slope <= 0 || n <= 0) return (0, 0);
            double area = Math.PI * diameter * diameter / 4;
            double v = Math.Pow(diameter / 4, 2.0 / 3.0) * Math.Sqrt(slope) / n;
            return (v * area, v);
        }

        // Flow area and wetted perimeter of a circular pipe flowing at depth y.
        static (double area, double perimeter) Section(double d, double y)
        {
            y = Math.Max(0, Math.Min(d, y));
            double theta = 2 * Math.Acos(1 - 2 * y / d);
            return (d * d / 8 * (theta - Math.Sin(theta)), d * theta / 2);
        }

        static double FlowAtDepth(double d, double y, double slope, double n)
        {
            var (a, p) = Section(d, y);
            if (a <= 0 || p <= 0) return 0;
            return a * Math.Pow(a / p, 2.0 / 3.0) * Math.Sqrt(slope) / n;
        }

        /// <summary>
        /// Normal depth (m) and velocity (m/s) for flow q in a circular pipe. Returns null when q is more than
        /// the pipe can carry part-full (it would surcharge).
        /// </summary>
        public static (double depth, double velocity)? NormalDepth(double d, double slope, double n, double q)
        {
            if (q <= 0) return (0, 0);
            if (d <= 0 || slope <= 0 || n <= 0) return null;
            double lo = 0, hi = 0.938 * d;   // a circular pipe carries its peak flow at about 0.938 D
            if (FlowAtDepth(d, hi, slope, n) < q) return null;
            for (int i = 0; i < 80; i++)
            {
                double mid = (lo + hi) / 2;
                if (FlowAtDepth(d, mid, slope, n) < q) lo = mid; else hi = mid;
            }
            double y = (lo + hi) / 2;
            var (area, _) = Section(d, y);
            return (y, area > 0 ? q / area : 0);
        }

        /// <summary>Friction slope (m/m) of a full circular pipe carrying q, Manning's equation.</summary>
        public static double FrictionSlopeFull(double d, double q, double n)
        {
            double area = Math.PI * d * d / 4;
            double k = area * Math.Pow(d / 4, 2.0 / 3.0) / n;
            return k > 0 ? Math.Pow(q / k, 2) : 0;
        }

        // ---------------------------------------------------------------- road design

        /// <summary>Longitudinal friction factor for stopping sight distance (IRC:SP:23 table, interpolated).</summary>
        public static double IrcFriction(double speedKmh)
        {
            double[] v = { 20, 30, 40, 50, 60, 65, 80, 100, 120 };
            double[] f = { 0.40, 0.40, 0.38, 0.37, 0.36, 0.36, 0.35, 0.35, 0.35 };
            if (speedKmh <= v[0]) return f[0];
            for (int i = 1; i < v.Length; i++)
                if (speedKmh <= v[i]) return f[i - 1] + (f[i] - f[i - 1]) * (speedKmh - v[i - 1]) / (v[i] - v[i - 1]);
            return f[f.Length - 1];
        }

        /// <summary>
        /// Stopping sight distance (m). standard "aashto": deceleration 3.4 m/s²; "irc": IRC friction table.
        /// grade is a fraction (+ uphill, - downhill).
        /// </summary>
        public static double StoppingDistance(double speedKmh, double grade, string standard, double reactionTime)
        {
            double friction = standard == "irc" ? IrcFriction(speedKmh) : 3.4 / 9.81;
            double denominator = 254 * Math.Max(0.05, friction + grade);
            return 0.278 * speedKmh * reactionTime + speedKmh * speedKmh / denominator;
        }

        /// <summary>Minimum K (m per % of grade change) for a crest or sag vertical curve, from the sight distance.</summary>
        public static double MinK(double ssd, bool crest, string standard)
        {
            if (crest) return ssd * ssd / (standard == "irc" ? 440.0 : 658.0);
            return ssd * ssd / (standard == "irc" ? 150 + 3.5 * ssd : 120 + 3.5 * ssd);
        }

        /// <summary>AASHTO maximum side friction for a design speed (km/h), interpolated.</summary>
        public static double AashtoSideFriction(double speedKmh)
        {
            double[] v = { 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
            double[] f = { 0.20, 0.18, 0.16, 0.15, 0.14, 0.14, 0.13, 0.12, 0.11, 0.09 };
            if (speedKmh <= v[0]) return f[0];
            for (int i = 1; i < v.Length; i++)
                if (speedKmh <= v[i]) return f[i - 1] + (f[i] - f[i - 1]) * (speedKmh - v[i - 1]) / (v[i] - v[i - 1]);
            return f[f.Length - 1];
        }
    }
}

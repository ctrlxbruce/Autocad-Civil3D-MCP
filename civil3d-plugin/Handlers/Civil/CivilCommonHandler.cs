using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace Civil3DMcp.Handlers
{
    /// <summary>
    /// Listing Civil objects and working with ANY Civil 3D style collection.
    /// Style categories are property names under CivilDocument.Styles, for example:
    ///   PointStyles, SurfaceStyles, AlignmentStyles, ProfileStyles, ProfileViewStyles,
    ///   SectionStyles, SectionViewStyles, SampleLineStyles, CorridorStyles, PipeStyles,
    ///   StructureStyles, ParcelStyles, FeatureLineStyles, MarkerStyles, LinkStyles, ShapeStyles,
    ///   LabelSetStyles.AlignmentLabelSetStyles, LabelSetStyles.ProfileLabelSetStyles,
    ///   LabelStyles.PointLabelStyles.LabelStyles, LabelStyles.SurfaceLabelStyles.ContourLabelStyles,
    ///   LabelStyles.AlignmentLabelStyles.MajorStationLabelStyles, ProfileViewBandSetStyles,
    ///   PartsListSet, TableStyles.SurfaceElevationTableStyles ...
    /// Use styles.categories to discover the exact names on your Civil 3D version.
    /// </summary>
    public static class CivilCommonHandler
    {
        [Tool("civil.list")]
        public static object ListCivil(Args a, Ctx c)
        {
            string kind = a.Str("kind", "surfaces").ToLowerInvariant();
            IEnumerable<ObjectId> ids;
            switch (kind)
            {
                case "surfaces": ids = Ctx.SafeIds(Reflect.Call(c.Civ, "GetSurfaceIds")); break;
                case "alignments": ids = Ctx.SafeIds(Reflect.Call(c.Civ, "GetAlignmentIds")); break;
                case "sites": ids = Ctx.SafeIds(Reflect.Call(c.Civ, "GetSiteIds")); break;
                case "pipe_networks": ids = Ctx.SafeIds(Reflect.Call(c.Civ, "GetPipeNetworkIds")); break;
                case "pressure_networks": ids = Ctx.SafeIds(Reflect.Call(c.Civ, "GetPressurePipeNetworkIds")); break;
                case "point_groups": ids = Ctx.SafeIds(Reflect.Get(c.Civ, "PointGroups")); break;
                case "corridors": ids = Ctx.SafeIds(Reflect.Get(c.Civ, "CorridorCollection")); break;
                case "assemblies": ids = Ctx.SafeIds(Reflect.Get(c.Civ, "AssemblyCollection")); break;
                case "subassemblies": ids = Ctx.SafeIds(Reflect.Get(c.Civ, "SubassemblyCollection")); break;
                case "profiles":
                    ids = Ctx.SafeIds(Reflect.Call(c.Civ, "GetAlignmentIds"))
                        .SelectMany(al => Ctx.SafeIds(Reflect.Call(c.Open(al), "GetProfileIds"))).ToList();
                    break;
                case "feature_lines": case "profile_views": case "section_views": case "sample_line_groups": case "parcels":
                    var map = new Dictionary<string, string>
                    {
                        ["feature_lines"] = "FeatureLine", ["profile_views"] = "ProfileView", ["section_views"] = "SectionView",
                        ["sample_line_groups"] = "SampleLineGroup", ["parcels"] = "Parcel"
                    };
                    var tn = map[kind];
                    ids = c.ModelSpace().Cast<ObjectId>().Where(i => i.ObjectClass.Name.EndsWith(tn, StringComparison.OrdinalIgnoreCase)
                                                               || i.ObjectClass.DxfName.Replace("_", "").EndsWith(tn, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (kind == "sample_line_groups")
                        ids = Ctx.SafeIds(Reflect.Call(c.Civ, "GetAlignmentIds"))
                            .SelectMany(al => Ctx.SafeIds(Reflect.Call(c.Open(al), "GetSampleLineGroupIds"))).ToList();
                    break;
                default: throw new ArgumentException("kind: surfaces, alignments, profiles, sites, pipe_networks, pressure_networks, point_groups, corridors, assemblies, subassemblies, feature_lines, profile_views, section_views, sample_line_groups, parcels");
            }
            return ids.Select(id =>
            {
                var o = c.Open(id);
                return new { handle = Ctx.H(id), name = Safe(() => Reflect.Get(o, "Name") as string), type = o.GetType().Name };
            }).ToList();
        }

        internal static T Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }

        [Tool("styles.categories")]
        public static object Categories(Args a, Ctx c)
        {
            object root = a.Has("path") ? Reflect.GetPath(c.Civ.Styles, a.Str("path")) : c.Civ.Styles;
            return root.GetType().GetProperties().Select(p => p.Name).OrderBy(x => x).ToList();
        }

        [Tool("styles.list")]
        public static object ListStyles(Args a, Ctx c) => c.StyleNames(a.NeedStr("category"));

        /// <summary>Create a style (or get existing) and optionally set properties on it.</summary>
        [Tool("styles.create")]
        public static object CreateStyle(Args a, Ctx c)
        {
            var category = a.NeedStr("category");
            var name = a.NeedStr("name");
            ObjectId id;
            try { id = c.StyleId(category, name); }
            catch (ArgumentException)
            {
                var coll = Reflect.GetPath(c.Civ.Styles, category);
                id = (ObjectId)Reflect.Call(coll, "Add", name);
            }
            object report = null;
            if (a.Has("props")) report = Props.SetMany(c.Open(id, true), a.Obj("props"), c);
            return new { handle = Ctx.H(id), name, props = report };
        }

        [Tool("styles.get")]
        public static object GetStyle(Args a, Ctx c)
        {
            var o = c.Open(c.StyleId(a.NeedStr("category"), a.NeedStr("name")));
            return a.Has("path") ? Conv.Plain(Reflect.GetPath(o, a.Str("path"))) : Props.GetAll(o);
        }

        [Tool("styles.set")]
        public static object SetStyle(Args a, Ctx c) =>
            Props.SetMany(c.Open(c.StyleId(a.NeedStr("category"), a.NeedStr("name")), true), a.Obj("props"), c);

        /// <summary>
        /// Show/hide/color a display component of a style (plan / model / profile / section view).
        /// Example: SurfaceStyles "Contours 1m and 5m" component "MajorContour" visible true color "1".
        /// method: GetDisplayStylePlan | GetDisplayStyleModel | GetDisplayStyleProfile | GetDisplayStyleSection
        /// </summary>
        [Tool("styles.display")]
        public static object StyleDisplay(Args a, Ctx c)
        {
            var st = c.Open(c.StyleId(a.NeedStr("category"), a.NeedStr("name")), true);
            var method = a.Str("view", "plan").ToLower() switch
            {
                "model" => "GetDisplayStyleModel", "profile" => "GetDisplayStyleProfile",
                "section" => "GetDisplayStyleSection", _ => "GetDisplayStylePlan"
            };
            var results = new Dictionary<string, object>();
            foreach (var comp in a.Strs("components"))
            {
                try
                {
                    var ds = Reflect.Call(st, method, comp);   // string → the right enum automatically
                    if (a.Has("visible")) Reflect.Set(ds, "Visible", a.Bool("visible"));
                    if (a.Has("color")) Reflect.Set(ds, "Color", Conv.ToColor(a.Node("color")));
                    if (a.Has("layer")) { c.EnsureLayer(a.Str("layer")); Reflect.Set(ds, "Layer", a.Str("layer")); }
                    if (a.Has("linetype")) { c.EnsureLinetype(a.Str("linetype")); Reflect.Set(ds, "Linetype", a.Str("linetype")); }
                    if (a.Has("lineweight")) Reflect.Set(ds, "Lineweight", Conv.ToLineWeight(a.Node("lineweight")));
                    results[comp] = Props.GetAll(ds);
                }
                catch (Exception ex) { results[comp] = "ERROR: " + (ex.InnerException?.Message ?? ex.Message); }
            }
            return results;
        }

        [Tool("civil.drawing_settings")]
        public static object DrawingSettings(Args a, Ctx c)
        {
            var settings = Reflect.Get(c.Civ, "Settings");
            var dwg = Reflect.Get(settings, "DrawingSettings");
            if (a.Has("props")) return Props.SetMany(dwg, a.Obj("props"), c);
            return new
            {
                units_zone = CivilCommonHandler.Safe(() => Props.GetAll(Reflect.Get(dwg, "UnitZoneSettings"))),
                ambient = CivilCommonHandler.Safe(() => Reflect.Names(Reflect.Get(settings, "DrawingSettings").GetType()))
            };
        }
    }
}

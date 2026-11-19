using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;

namespace Civil3DMcp.Handlers
{
    public static class LayoutHandler
    {
        [Tool("layouts.list", ReadOnly = true)]
        public static object ListLayouts(Args a, Ctx c)
        {
            var dict = c.Open<DBDictionary>(c.Db.LayoutDictionaryId);
            var cur = LayoutManager.Current.CurrentLayout;
            var layouts = new List<Layout>();
            foreach (DBDictionaryEntry e in dict) layouts.Add(c.Open<Layout>(e.Value));
            return layouts.OrderBy(l => l.TabOrder)
                .Select(l => new { name = l.LayoutName, tab = l.TabOrder, current = l.LayoutName == cur, paper = l.CanonicalMediaName, device = l.PlotConfigurationName })
                .ToList();
        }

        [Tool("layouts.create", NoPreview = "switches layout tabs")]
        public static object CreateLayout(Args a, Ctx c)
        {
            var id = LayoutManager.Current.CreateLayout(a.NeedStr("name"));
            if (a.Bool("make_current", true)) LayoutManager.Current.CurrentLayout = a.Str("name");
            return new { handle = Ctx.H(id) };
        }

        [Tool("layouts.set_current", NoPreview = "switches layout tabs")]
        public static object SetCurrentLayout(Args a, Ctx c) { LayoutManager.Current.CurrentLayout = a.NeedStr("name"); return new { current = a.Str("name") }; }

        [Tool("layouts.delete", NoPreview = "removes a layout tab")]
        public static object DeleteLayout(Args a, Ctx c) { LayoutManager.Current.DeleteLayout(a.NeedStr("name")); return new { deleted = true }; }

        /// <summary>Viewport on a layout: center/size in paper units, view_center in model units, scale e.g. 1/500 = 0.002.</summary>
        [Tool("layouts.viewport")]
        public static object AddViewport(Args a, Ctx c)
        {
            var layoutName = a.Str("layout", LayoutManager.Current.CurrentLayout);
            var lid = LayoutManager.Current.GetLayoutId(layoutName);
            var lay = c.Open<Layout>(lid);
            var btr = c.Open<BlockTableRecord>(lay.BlockTableRecordId, true);
            var vp = new Viewport
            {
                CenterPoint = a.Pt("center"),
                Width = a.Dbl("width"),
                Height = a.Dbl("height"),
            };
            vp.SetDatabaseDefaults(c.Db);
            var id = btr.AppendEntity(vp);
            c.Tr.AddNewlyCreatedDBObject(vp, true);
            if (a.Has("view_center")) vp.ViewCenter = Conv.ToPoint2d(a.Node("view_center"));
            if (a.Has("scale")) vp.CustomScale = a.Dbl("scale");
            if (a.Has("twist")) vp.TwistAngle = a.Deg("twist");
            vp.On = true;
            if (a.Bool("lock")) vp.Locked = true;
            if (a.Has("layer")) { c.EnsureLayer(a.Str("layer")); vp.Layer = a.Str("layer"); }
            return new { handle = Ctx.H(id) };
        }

        /// <summary>Plot the current layout (or model) to PDF using "DWG To PDF.pc3".</summary>
        [Tool("layouts.plot_pdf", NoPreview = "writes a PDF file")]
        public static object PlotPdf(Args a, Ctx c)
        {
            var output = a.NeedStr("output_path");
            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                throw new InvalidOperationException("Another plot is running.");

            var btr = c.Open<BlockTableRecord>(c.Db.CurrentSpaceId);
            var lo = c.Open<Layout>(btr.LayoutId);
            var ps = new PlotSettings(lo.ModelType);
            ps.CopyFrom(lo);
            var psv = PlotSettingsValidator.Current;
            // paper: canonical name from layouts.list_papers, e.g. "ISO_full_bleed_A1_(841.00_x_594.00_MM)"
            psv.SetPlotConfigurationName(ps, a.Str("device", "DWG To PDF.pc3"), a.Str("paper", "ISO_full_bleed_A3_(420.00_x_297.00_MM)"));
            psv.RefreshLists(ps);
            bool extents = a.Str("area", "extents").ToLowerInvariant() != "layout";
            psv.SetPlotType(ps, extents ? Autodesk.AutoCAD.DatabaseServices.PlotType.Extents : Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
            psv.SetUseStandardScale(ps, true);
            if (extents) { psv.SetStdScaleType(ps, StdScaleType.ScaleToFit); psv.SetPlotCentered(ps, true); }
            else psv.SetStdScaleType(ps, StdScaleType.StdScale1To1);
            if (a.Has("style_table")) psv.SetCurrentStyleSheet(ps, a.Str("style_table"));   // e.g. "monochrome.ctb"

            var pi = new PlotInfo { Layout = btr.LayoutId, OverrideSettings = ps };
            var piv = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
            piv.Validate(pi);

            var oldBg = Application.GetSystemVariable("BACKGROUNDPLOT");
            Application.SetSystemVariable("BACKGROUNDPLOT", (short)0);
            try
            {
                using var pe = PlotFactory.CreatePublishEngine();
                using var ppd = new PlotProgressDialog(false, 1, true);
                ppd.OnBeginPlot();
                ppd.IsVisible = false;
                pe.BeginPlot(ppd, null);
                pe.BeginDocument(pi, c.Doc.Name, null, 1, true, output);
                ppd.OnBeginSheet();
                pe.BeginPage(new PlotPageInfo(), pi, true, null);
                pe.BeginGenerateGraphics(null);
                pe.EndGenerateGraphics(null);
                pe.EndPage(null);
                ppd.OnEndSheet();
                pe.EndDocument(null);
                ppd.OnEndPlot();
                pe.EndPlot(null);
            }
            finally { Application.SetSystemVariable("BACKGROUNDPLOT", oldBg); }
            return new { pdf = output };
        }

        [Tool("layouts.list_papers", ReadOnly = true)]
        public static object ListPapers(Args a, Ctx c)
        {
            var btr = c.Open<BlockTableRecord>(c.Db.CurrentSpaceId);
            var lo = c.Open<Layout>(btr.LayoutId);
            var ps = new PlotSettings(lo.ModelType);
            ps.CopyFrom(lo);
            var psv = PlotSettingsValidator.Current;
            psv.SetPlotConfigurationName(ps, a.Str("device", "DWG To PDF.pc3"), null);
            psv.RefreshLists(ps);
            return psv.GetCanonicalMediaNameList(ps).Cast<string>().ToList();
        }
    }
}

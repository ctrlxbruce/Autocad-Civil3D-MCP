using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;

namespace Civil3DMcp.Handlers
{
    /// <summary>Single-line text, MText, text styles, tables.</summary>
    public static class TextHandler
    {
        static AttachmentPoint Justify(string j)
        {
            switch ((j ?? "left").ToUpperInvariant())
            {
                case "TL": return AttachmentPoint.TopLeft;
                case "TC": case "TOPCENTER": return AttachmentPoint.TopCenter;
                case "TR": return AttachmentPoint.TopRight;
                case "ML": return AttachmentPoint.MiddleLeft;
                case "MC": case "MIDDLE": return AttachmentPoint.MiddleCenter;
                case "MR": return AttachmentPoint.MiddleRight;
                case "BL": return AttachmentPoint.BottomLeft;
                case "BC": return AttachmentPoint.BottomCenter;
                case "BR": return AttachmentPoint.BottomRight;
                case "CENTER": return AttachmentPoint.BaseCenter;
                case "RIGHT": return AttachmentPoint.BaseRight;
                default: return AttachmentPoint.BaseLeft;
            }
        }

        [Tool("annotation.text")]
        public static object AddText(Args a, Ctx c)
        {
            var t = new DBText
            {
                TextString = a.NeedStr("text"),
                Position = a.Pt("position"),
                Height = a.Dbl("height", c.Db.Textsize),
                Rotation = a.Deg("rotation"),
                TextStyleId = c.TextStyleId(a.Str("style")),
            };
            if (a.Has("width_factor")) t.WidthFactor = a.Dbl("width_factor");
            if (a.Has("oblique")) t.Oblique = a.Deg("oblique");
            var id = c.Add(t, a);
            var j = Justify(a.Str("justify"));
            if (j != AttachmentPoint.BaseLeft)
            {
                t.Justify = j;
                t.AlignmentPoint = a.Pt("position");
                t.AdjustAlignment(c.Db);
            }
            return new { handle = Ctx.H(id) };
        }

        [Tool("annotation.mtext")]
        public static object AddMText(Args a, Ctx c)
        {
            var m = new MText
            {
                Contents = a.NeedStr("text"),     // supports \P for new line and MText codes
                Location = a.Pt("position"),
                TextHeight = a.Dbl("height", c.Db.Textsize),
                Width = a.Dbl("width", 0),
                Rotation = a.Deg("rotation"),
                TextStyleId = c.TextStyleId(a.Str("style")),
                Attachment = Justify(a.Str("justify", "TL")),
            };
            if (a.Has("line_spacing")) m.LineSpacingFactor = a.Dbl("line_spacing");
            var id = c.Add(m, a);
            if (a.Has("background_fill"))
            {
                m.BackgroundFill = true;
                m.UseBackgroundColor = a.Str("background_fill") == "drawing";
                if (!m.UseBackgroundColor) m.BackgroundFillColor = Conv.ToColor(a.Node("background_fill"));
                m.BackgroundScaleFactor = a.Dbl("background_scale", 1.5);
            }
            return new { handle = Ctx.H(id) };
        }

        [Tool("annotation.edit_text")]
        public static object EditText(Args a, Ctx c)
        {
            var o = c.Open(a.NeedStr("handle"), true);
            if (o is DBText t) { if (a.Has("text")) t.TextString = a.Str("text"); if (a.Has("height")) t.Height = a.Dbl("height"); }
            else if (o is MText m) { if (a.Has("text")) m.Contents = a.Str("text"); if (a.Has("height")) m.TextHeight = a.Dbl("height"); if (a.Has("width")) m.Width = a.Dbl("width"); }
            else throw new ArgumentException("Not a text object.");
            return new { ok = true };
        }

        [Tool("styles.text_style")]
        public static object TextStyle(Args a, Ctx c)
        {
            var name = a.NeedStr("name");
            var tbl = c.Open<TextStyleTable>(c.Db.TextStyleTableId);
            TextStyleTableRecord rec;
            if (tbl.Has(name)) rec = c.Open<TextStyleTableRecord>(tbl[name], true);
            else
            {
                tbl.UpgradeOpen();
                rec = new TextStyleTableRecord { Name = name };
                tbl.Add(rec);
                c.Tr.AddNewlyCreatedDBObject(rec, true);
            }
            if (a.Has("font"))
            {
                var f = a.Str("font");
                if (f.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)) rec.FileName = f;
                else
                {
                    var face = f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? f.Substring(0, f.Length - 4) : f;
                    rec.Font = new FontDescriptor(face, a.Bool("bold"), a.Bool("italic"), 0, 0);
                }
            }
            if (a.Has("big_font")) rec.BigFontFileName = a.Str("big_font");
            if (a.Has("height")) rec.TextSize = a.Dbl("height");      // 0 = ask each time
            if (a.Has("width_factor")) rec.XScale = a.Dbl("width_factor");
            if (a.Has("oblique")) rec.ObliquingAngle = a.Deg("oblique");
            if (a.Has("annotative")) rec.Annotative = a.Bool("annotative") ? AnnotativeStates.True : AnnotativeStates.False;
            if (a.Bool("make_current")) c.Db.Textstyle = rec.ObjectId;
            return new { name, handle = Ctx.H(rec.ObjectId) };
        }

        [Tool("styles.list_text_styles")]
        public static object ListTextStyles(Args a, Ctx c) =>
            c.Open<TextStyleTable>(c.Db.TextStyleTableId).Cast<ObjectId>().Select(id => c.Open<TextStyleTableRecord>(id))
             .Select(r => new { name = r.Name, font = string.IsNullOrEmpty(r.FileName) ? r.Font.TypeFace : r.FileName, height = r.TextSize, width_factor = r.XScale })
             .ToList();

        [Tool("annotation.table")]
        public static object AddTable(Args a, Ctx c)
        {
            var rows = a.Arr("data");
            int nr = Math.Max(rows.Count, a.Int("rows", 1));
            int nc = Math.Max(rows.Select(r => (r as System.Text.Json.Nodes.JsonArray)?.Count ?? 1).DefaultIfEmpty(1).Max(), a.Int("columns", 1));
            var t = new Table { TableStyle = c.Db.Tablestyle, Position = a.Pt("position") };
            t.SetSize(nr, nc);
            t.SetRowHeight(a.Dbl("row_height", 3));
            t.SetColumnWidth(a.Dbl("column_width", 20));
            for (int r = 0; r < rows.Count; r++)
                if (rows[r] is System.Text.Json.Nodes.JsonArray ra)
                    for (int k = 0; k < ra.Count; k++)
                    {
                        t.Cells[r, k].TextString = Conv.NodeToString(ra[k]) ?? "";
                        if (a.Has("text_height")) t.Cells[r, k].TextHeight = a.Dbl("text_height");
                    }
            var id = c.Add(t, a);
            t.GenerateLayout();
            return new { handle = Ctx.H(id) };
        }
    }
}

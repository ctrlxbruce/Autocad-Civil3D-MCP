_CODE_DESC = (
    "C# runs INSIDE Civil 3D with the full AutoCAD + Civil 3D .NET API (Roslyn). Globals: Document, CivilDoc, "
    "Database, Transaction (already open), Editor, Ctx (helpers: Ctx.Find(\"EG\") → ObjectId by name/handle, "
    "Ctx.Add(entity) → adds to current space, Ctx.StyleId(category,name), Ctx.EnsureLayer(name), Ctx.ModelSpace()), "
    "Inputs (dict of your inputs), Print(x) for notes. Imported: System, System.Linq, Autodesk.AutoCAD.DatabaseServices/"
    "Geometry/ApplicationServices/EditorInput/Colors, Autodesk.Civil, Autodesk.Civil.ApplicationServices, "
    "Autodesk.Civil.DatabaseServices, Civil3DMcp (Conv.ToPoint3d, Conv.ToColor...). 'Entity' = AutoCAD Entity, "
    "'Surface' = Civil Surface; use full names for styles (Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle). "
    "Do NOT start/commit transactions yourself. End with `return <value>;` to get data back as JSON. "
    "Check skills_search first for a ready template."
)
_PARAMS = {"code": ("string", "C# script body", True), "inputs": ("object", "Values available as Inputs[\"key\"]")}

TOOLS = {
    "code.execute": ("Run C# that CHANGES the drawing (transaction is committed). " + _CODE_DESC, _PARAMS),
    "code.query": ("Run C# READ-ONLY (transaction always rolled back, nothing changes). Use for questions/reports. " + _CODE_DESC, _PARAMS),
}

# Handled by the Python server itself (no Civil 3D call)
LOCAL_TOOLS = {
    "skills.list": ("List the C# skill templates (by category: surfaces, points, alignments, drawing, geometry, pipes, workflows).",
                    {"category": ("string", "Optional category")}),
    "skills.search": ("Search skill templates by words, e.g. 'contour smoothing', 'import csv', 'volume'.",
                      {"query": ("string", "Search words", True)}),
    "skills.read": ("Read a skill template (code + example inputs) to adapt and run with code_execute / code_query.",
                    {"name": ("string", "Skill name, e.g. 'contour_style' or 'surfaces/contour_style'", True)}),
}

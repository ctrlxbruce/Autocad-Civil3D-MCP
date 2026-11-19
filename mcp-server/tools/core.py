TOOLS = {
    "system.ping": ("Check that Civil 3D and the plugin are running.", {}),
    "system.list_tools": ("List every tool name the plugin knows.", {}),
    "doc.info": ("Info about the open drawing: name, units, current layer/text/dim style, extents.", {}),
    "doc.run_command": (
        "FALLBACK for anything without a dedicated tool: send text to the Civil 3D command line exactly as typed. "
        "Use '-' command versions to avoid dialogs (e.g. '-LAYER', '-PLOT'). Separate inputs with spaces or \\n. "
        "Civil 3D command names usually start with Aecc (e.g. '_AeccLandXMLIn', '_AeccSetNetworkCatalog'). "
        "Examples: 'FILLET R 2 ', '_TRIM', 'REVCLOUD', '_AeccLandXMLIn'. "
        "Blocked for safety: AutoLISP, and commands that start programs, load code (NETLOAD, SCRIPT, VBA) or quit.",
        {"command": ("string", "Command text", True)}),
    "doc.get_sysvar": ("Read a system variable (e.g. DIMSCALE, LTSCALE, PDMODE).", {"name": ("string", "Variable name", True)}),
    "doc.set_sysvar": ("Set a system variable. Security variables (SECURELOAD, TRUSTEDPATHS...) can't be changed.", {"name": ("string", "Variable name", True), "value": ("string", "New value", True)}),
    "doc.zoom_extents": ("Zoom to extents.", {}),
    "doc.summary": ("Short, size-capped picture of a drawing (good first call on large drawings): object counts by type and "
                    "layer, extents, a spatial grid of where objects are, Civil 3D object counts, xrefs and layouts.",
                    {"top": ("integer", "How many types / layers to list (default 15)"),
                     "grid": ("integer", "Spatial grid size, default 4")}),
    "doc.save": ("Save the drawing (QSAVE) or Save As when path is given.", {"path": ("string", "Optional full path ending in .dwg (no quotes)")}),
    "doc.undo": ("Undo Claude's own last changes in the active drawing (one step = one tool call that changed it; a "
                 "zoom after it is undone with it). Refuses when the drawing was changed another way since (by the "
                 "user, a command-line command, a layout or system-variable change), so it never undoes the user's "
                 "work; then the user can press Ctrl+Z in Civil 3D.",
                 {"steps": ("integer", "How many of Claude's changes to undo (default 1)")}),
    "query.list": ("List objects in the current space with handle, type, layer and name. Filter by type (Line, Polyline, "
                   "Circle, TinSurface, Alignment, CogoPoint, ...) and/or layer.",
                   {"type": ("string", "Class name filter"), "layer": ("string", "Layer filter"), "limit": ("integer", "Max items (default 200)")}),
    "query.count_by_type": ("Count objects in the current space by type.", {}),
    "props.get": ("Read ALL properties of any object (entity, Civil object, style) by handle or Civil object name. "
                  "Optional dotted path to read one value, e.g. 'ContourStyle.MajorContourInterval'.",
                  {"handle": ("string", "Handle or name", True), "path": ("string", "Optional property path")}),
    "props.set": ("Set ANY properties on one or more objects. props example: {\"Layer\":\"C-ROAD\",\"Color\":\"1\","
                  "\"Elevation\":100,\"StyleId\":\"<style handle>\"}. Dotted paths allowed. ANGLES ARE IN RADIANS here.",
                  {"handle": ("string", "Handle or name"), "handles": ("strings", "Several handles"),
                   "props": ("object", "Property name → value", True)}),
    "props.methods": ("List the API methods available on an object (to use with props_call).", {"handle": ("string", "Handle or name", True)}),
    "props.call": ("Call ANY Civil 3D / AutoCAD API method on an object, e.g. method 'RaiseSurface' args [2.5]. "
                   "Strings are converted to enums, handles to ObjectIds, [x,y,z] to points.",
                   {"handle": ("string", "Handle or name", True), "method": ("string", "Method name", True),
                    "args": ("array", "Arguments in order")}),
    "entity.delete": ("Delete objects.", {"handles": ("strings", "Handles", True)}),
    "measure.distance": ("Distance, 2D distance, angle and deltas between two points.",
                         {"p1": ("point", "First point", True), "p2": ("point", "Second point", True)}),
    "measure.entity": ("Length, area, start/end and extents of an object.", {"handle": ("string", "Handle", True)}),
}

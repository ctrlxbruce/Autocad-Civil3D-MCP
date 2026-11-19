"""Tool catalogue. Each module defines TOOLS = {"plugin.tool.name": (description, {param: spec})}.

Param spec: (type, description) or (type, description, True) for required.
Types: string, number, integer, boolean, object, array,
       point  -> [x, y] or [x, y, z]
       points -> [[x, y], [x, y, z], ...]
       strings / numbers -> list of those
"""
from . import code, core, drafting, modify, layers, annotation, blocks, layouts
from . import civil_points, civil_surfaces, civil_alignments, civil_corridors, civil_pipes, civil_styles
from . import civil_pressure, civil_hydraulics, civil_quantities, preview
from . import meta

MODULES = [code, core, drafting, modify, layers, annotation, blocks, layouts,
           civil_points, civil_surfaces, civil_alignments, civil_corridors, civil_pipes, civil_styles,
           civil_pressure, civil_hydraulics, civil_quantities, preview]


LOCAL = set(code.LOCAL_TOOLS) | set(preview.LOCAL_TOOLS)

# Which tools load in each mode (CIVIL3D_MCP_MODE). Fewer tools = fewer tokens at the start of every chat.
CODE_MODE = {"system.ping", "doc.info", "doc.run_command", "code.execute", "code.query",
             "skills.list", "skills.search", "skills.read"}
LEAN_MODE = CODE_MODE | {
    "query.list", "query.count_by_type", "props.get", "props.set", "props.methods", "props.call", "entity.delete",
    "measure.distance", "measure.entity", "layers.list", "layers.set", "drafting.line", "drafting.polyline",
    "drafting.circle", "annotation.text", "annotation.mtext", "dim.aligned", "civil.list",
    "styles.categories", "styles.list", "styles.get", "styles.set", "points.import", "surface.info",
    "doc.summary", "doc.preview", "doc.undo",
}
# Search mode lists these plus tools.search / tools.read / tools.call (meta.py); every other tool is reached by name.
SEARCH_MODE = {"system.ping", "doc.info", "doc.summary", "query.list", "props.get", "civil.list", "doc.preview"}
META = set(meta.TOOLS)
MODES = ("full", "lean", "code", "search")


# MCP tool annotations, so Claude can tell reading from changing. Claude Desktop / Claude Code use them when asking
# for permission. Read-only tools never change the drawing (code.query always rolls back). Additive tools only add
# new objects or data. Everything else may change or remove existing objects or files ("destructive").
READ_ONLY = {
    "alignment.info", "alignment.point_at", "alignment.station_offset", "assembly.info", "assembly.list_stock",
    "blocks.list", "civil.list", "code.query", "doc.get_sysvar", "doc.info", "doc.preview", "doc.summary",
    "hydrology.catchment_area", "hydrology.low_point", "hydrology.rational", "hydrology.time_of_concentration",
    "layers.list", "layouts.list", "layouts.list_papers", "linetypes.list", "measure.distance", "measure.entity",
    "parcels.list", "pipes.hydraulics", "pipes.network_info", "pipes.parts_lists", "pipes.qc",
    "points.description_keys", "points.get", "points.list", "points.list_file_formats",
    "pressure.check_cover", "pressure.network_info", "pressure.parts_lists", "profile.elevation_at", "profile.info",
    "props.get", "props.methods", "qc.alignment", "qc.profile_k_values", "qc.stopping_distance",
    "qty.earthwork", "qty.pipe_lengths", "query.count_by_type", "query.list", "sections.sample_line_info",
    "skills.list", "skills.read", "skills.search", "styles.categories", "styles.dim_style_get", "styles.get",
    "styles.list", "styles.list_dim_styles", "styles.list_text_styles", "surface.elevation_at", "surface.info",
    "system.list_tools", "system.ping",
}
ADDITIVE = {
    "drafting.arc", "drafting.circle", "drafting.donut", "drafting.ellipse", "drafting.hatch", "drafting.line",
    "drafting.point", "drafting.polygon", "drafting.polyline", "drafting.polyline3d", "drafting.ray",
    "drafting.rectangle", "drafting.spline", "drafting.xline",
    "dim.aligned", "dim.angular", "dim.angular_2lines", "dim.arc_length", "dim.diameter", "dim.linear",
    "dim.ordinate", "dim.polyline_segments", "dim.radius",
    "annotation.mleader", "annotation.mtext", "annotation.table", "annotation.text",
    "blocks.attach_xref", "blocks.insert", "layouts.create", "layouts.viewport", "linetypes.load",
    "layers.set_current", "doc.zoom_extents",
    "points.create", "points.import", "point_groups.create",
    "surface.create", "surface.add_points", "surface.add_point_groups", "surface.add_breaklines",
    "surface.add_contours", "surface.add_boundary", "surface.add_dem", "surface.import_tin_file",
    "surface.volume", "surface.extract_contours",
    "alignment.from_polyline", "alignment.from_pis", "alignment.offset", "alignment.add_design_speed",
    "profile.from_surface", "profile.create_layout", "profile.view_create", "profile.add_pvi",
    "sections.sample_lines", "sections.create_views", "site.create", "featureline.from_entity", "styles.create",
    "assembly.create", "assembly.add_subassembly", "corridor.create", "corridor.add_surface",
    "pipes.network_create", "pipes.add_pipe", "pipes.add_structure", "pipes.connect", "pipes.interference_check",
    "pressure.network_create", "pressure.add_pipe", "pressure.add_fitting", "pressure.add_appurtenance",
    "modify.copy", "modify.offset", "modify.array_rect", "modify.array_polar", "hydrology.flow_path",
}
# These can reach beyond the drawing (command line, C# code), so they don't claim a closed world.
OPEN_WORLD = {"doc.run_command", "code.execute"}


def hints(name: str) -> dict:
    """ToolAnnotations fields for one plugin tool name."""
    if name in meta.HINTS:
        return dict(meta.HINTS[name])
    if name in READ_ONLY:
        return {"readOnlyHint": True, "openWorldHint": False}
    out = {"readOnlyHint": False, "destructiveHint": name not in ADDITIVE}
    if name not in OPEN_WORLD:
        out["openWorldHint"] = False
    return out


def all_tools(mode: str = "full") -> dict:
    if mode not in MODES:
        raise ValueError(f"Unknown CIVIL3D_MCP_MODE '{mode}'. Use one of: {', '.join(MODES)}.")
    only = {"code": CODE_MODE, "lean": LEAN_MODE, "search": SEARCH_MODE}.get(mode)
    out = {}
    for m in MODULES:
        items = list(m.TOOLS.items()) + list(getattr(m, "LOCAL_TOOLS", {}).items())
        for name, spec in items:
            if name in out:
                raise ValueError(f"duplicate tool {name}")
            out[name] = spec
    if mode == "search":
        groups = meta.groups_text(out)
        out = {n: s for n, s in out.items() if n in only}
        for name, (desc, params) in meta.TOOLS.items():
            out[name] = (desc.replace("{groups}", groups), params)
        return out
    return {n: s for n, s in out.items() if only is None or n in only}

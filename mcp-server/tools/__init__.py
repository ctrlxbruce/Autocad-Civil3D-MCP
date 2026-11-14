"""Tool catalogue. Each module defines TOOLS = {"plugin.tool.name": (description, {param: spec})}.

Param spec: (type, description) or (type, description, True) for required.
Types: string, number, integer, boolean, object, array,
       point  -> [x, y] or [x, y, z]
       points -> [[x, y], [x, y, z], ...]
       strings / numbers -> list of those
"""
from . import code, core, drafting, modify, layers, annotation, blocks, layouts
from . import civil_points, civil_surfaces, civil_alignments, civil_corridors, civil_pipes, civil_styles

MODULES = [code, core, drafting, modify, layers, annotation, blocks, layouts,
           civil_points, civil_surfaces, civil_alignments, civil_corridors, civil_pipes, civil_styles]


LOCAL = set(code.LOCAL_TOOLS)

# Which tools load in each mode (CIVIL3D_MCP_MODE). Fewer tools = fewer tokens at the start of every chat.
CODE_MODE = {"system.ping", "doc.info", "doc.run_command", "code.execute", "code.query",
             "skills.list", "skills.search", "skills.read"}
LEAN_MODE = CODE_MODE | {
    "query.list", "query.count_by_type", "props.get", "props.set", "props.methods", "props.call", "entity.delete",
    "measure.distance", "measure.entity", "layers.list", "layers.set", "drafting.line", "drafting.polyline",
    "drafting.circle", "annotation.text", "annotation.mtext", "dim.aligned", "civil.list",
    "styles.categories", "styles.list", "styles.get", "styles.set", "points.import", "surface.info",
}


def all_tools(mode: str = "full") -> dict:
    out = {}
    for m in MODULES:
        items = list(m.TOOLS.items()) + list(getattr(m, "LOCAL_TOOLS", {}).items())
        for name, spec in items:
            if mode == "code" and name not in CODE_MODE:
                continue
            if mode == "lean" and name not in LEAN_MODE:
                continue
            if name in out:
                raise ValueError(f"duplicate tool {name}")
            out[name] = spec
    return out

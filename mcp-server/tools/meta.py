"""Search mode (CIVIL3D_MCP_MODE=search): Claude sees a few core tools plus these three, and reaches every other
tool by name. Handled by the Python server itself; see server.py.

tools.read only runs read-only tools and tools.call runs the rest, so Claude Desktop and Claude Code can still ask
before anything that changes the drawing.
"""
from __future__ import annotations

import re

_RUN = {"name": ("string", "Tool name from tools_search, e.g. 'pipes_add_pipe'", True),
        "args": ("object_or_json", "The tool's parameters, e.g. {\"start\": [0, 0], \"end\": [10, 0]}")}

TOOLS = {
    "tools.search": (
        "Find Civil 3D tools by words (e.g. 'pipe slope', 'dimension radius', 'import points csv', 'cut fill volume') "
        "or list a whole group. Returns each tool's name, what it does and its parameters. Run read-only tools with "
        "tools_read and the others with tools_call. Groups: {groups}.",
        {"query": ("string", "Search words"),
         "group": ("string", "List every tool of one group, e.g. 'pipes'"),
         "limit": ("integer", "Max tools to return (default 6)")}),
    "tools.read": ("Run a READ-ONLY Civil 3D tool found with tools_search (it never changes the drawing).", _RUN),
    "tools.call": ("Run any Civil 3D tool found with tools_search, including ones that add, change or delete "
                   "objects. Prefer tools_read for read-only tools.", _RUN),
}

HINTS = {
    "tools.search": {"readOnlyHint": True, "openWorldHint": False},
    "tools.read": {"readOnlyHint": True, "openWorldHint": False},
    "tools.call": {"readOnlyHint": False, "destructiveHint": True},
}

_STOP = {"a", "an", "and", "at", "by", "for", "from", "in", "into", "of", "on", "or", "the", "to", "with", "tool",
         "tools", "civil", "3d"}


def group_of(name: str) -> str:
    return name.split(".", 1)[0]


def groups_text(names) -> str:
    counts: dict[str, int] = {}
    for n in names:
        counts[group_of(n)] = counts.get(group_of(n), 0) + 1
    return ", ".join(f"{g} ({c})" for g, c in sorted(counts.items()))


# Everyday words that don't appear in tool names or descriptions.
_ALSO = {"water": "pressure", "sewer": "pipes", "storm": "pipes", "drainage": "pipes", "drain": "pipes",
         "manhole": "structure", "road": "corridor", "highway": "corridor", "colour": "color", "erase": "delete",
         "remove": "delete", "rename": "set", "print": "plot", "cogo": "points", "survey": "points",
         "tin": "surface", "ground": "surface", "terrain": "surface", "dimension": "dim", "dimensions": "dim",
         "label": "labels", "chainage": "station", "earthwork": "earthwork", "rain": "rainfall",
         "csharp": "code", "script": "code"}


def words(text: str) -> list[str]:
    out = []
    for w in re.findall(r"[a-z0-9#]+", text.lower()):
        w = "csharp" if w == "c#" else w.strip("#")
        if w and w not in _STOP and len(w) > 1:
            out.append(w)
            if w in _ALSO and _ALSO[w] not in out:
                out.append(_ALSO[w])
    return out


def search(catalogue: dict, query: str = "", group: str = "", limit: int = 6) -> tuple[list[str], int]:
    """Plugin tool names that best match, and how many matched in all."""
    if group:
        g = group.strip().lower().replace("_", ".").split(".")[0]
        found = [n for n in catalogue if group_of(n) == g]
        q = words(query)
        if q:
            found = [n for n in found if _score(n, catalogue[n], q) > 0] or found
        return found[:limit], len(found)
    q = words(query)
    if not q:
        return [], 0
    scored = sorted(((_score(n, catalogue[n], q), n) for n in catalogue), key=lambda x: -x[0])
    found = [n for s, n in scored if s > 0]
    return found[:limit], len(found)


def _score(name: str, spec: tuple, q: list[str]) -> float:
    desc, params = spec[0], spec[1]
    name_words = re.findall(r"[a-z0-9]+", name.lower())
    hay = desc.lower()
    param_hay = " ".join(f"{p} {s[1]}" for p, s in params.items()).lower()
    score, hits = 0.0, 0
    for w in q:
        hit = False
        if w in name_words:
            score, hit = score + 6, True                          # exact word in the tool name
        elif any(nw.startswith(w) or w.startswith(nw) for nw in name_words if len(nw) > 2):
            score, hit = score + 4, True                          # 'pipe' ~ 'pipes', 'dimension' ~ 'dim'
        if w in hay:
            score, hit = score + 1 + min(hay.count(w), 3) * 0.5, True
        elif w in param_hay:
            score, hit = score + 1, True                          # e.g. 'color' is a parameter of layers_set
        hits += hit
    return score * hits / len(q)                                  # favour tools that match every word


def resolve(catalogue: dict, name: str) -> str | None:
    """Plugin name for 'pipes_add_pipe', 'pipes.add_pipe' or 'mcp__civil3d__pipes_add_pipe'."""
    n = name.strip()
    for prefix in ("mcp__civil3d__", "civil3d_", "civil3d:"):
        if n.startswith(prefix):
            n = n[len(prefix):]
    if n in catalogue:
        return n
    by_mcp = {k.replace(".", "_"): k for k in catalogue}
    return by_mcp.get(n.replace(".", "_"))

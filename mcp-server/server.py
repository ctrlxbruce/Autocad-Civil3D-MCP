"""Civil 3D MCP server.

Claude Desktop starts this program. It lists the tools to Claude and forwards every
call to the Civil 3D plugin over 127.0.0.1:8765 (see bridge.py).
"""
from __future__ import annotations

import asyncio
import json
import sys
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import anyio  # noqa: E402
import mcp.types as types  # noqa: E402
from mcp.server.lowlevel import Server  # noqa: E402
from mcp.server.stdio import stdio_server  # noqa: E402

import os  # noqa: E402

import bridge  # noqa: E402
import skills_lib  # noqa: E402
from tools import LOCAL, META, MODES, READ_ONLY, ADDITIVE, all_tools, hints, meta  # noqa: E402

MODE = os.environ.get("CIVIL3D_MCP_MODE", "full").strip().lower()  # full | lean | code | search
if MODE not in MODES:
    print(f"civil3d-mcp: unknown CIVIL3D_MCP_MODE '{MODE}', using 'full'. Modes: {', '.join(MODES)}",
          file=sys.stderr)
    MODE = "full"
# Results longer than this are shortened (long lists cut to a prefix) so a big drawing can't flood the chat.
MAX_RESULT_CHARS = int(os.environ.get("CIVIL3D_MCP_MAX_RESULT_CHARS", "60000"))
# Drawing lock: tools that can change something only run on the drawing Claude last looked at (doc_info).
DRAWING_LOCK = os.environ.get("CIVIL3D_MCP_DRAWING_LOCK", "1").strip() != "0"
SETS_LOCK = {"doc.info", "doc.summary"}


class DrawingLock:
    """Remembers the drawing Claude last looked at ({"id", "name"} from the plugin) for this chat."""

    def __init__(self):
        self.seen: dict | None = None
        self._guard = threading.Lock()

    def expect(self, tool: str) -> dict | None:
        """What to send as expect_doc: only for tools that can change something, once a drawing is known."""
        if not DRAWING_LOCK or tool in READ_ONLY:
            return None
        with self._guard:
            return dict(self.seen) if self.seen else None

    def update(self, tool: str, doc: dict | None, ok: bool) -> str | None:
        """Record the active drawing from a reply. Returns a note for Claude when it differs from the one seen."""
        if not doc or not doc.get("id"):
            return None
        with self._guard:
            seen = self.seen
            if seen is None or (ok and tool in SETS_LOCK):
                self.seen = {"id": doc["id"], "name": doc.get("name")}
                if seen is not None and seen["id"] != doc["id"]:
                    return (f"Note: this is a different drawing from '{seen.get('name')}' that you worked on "
                            f"before. Tools that change things now act on '{doc.get('name')}'.")
                return None
            if seen["id"] != doc["id"] and ok:
                return (f"Note: the active drawing is now '{doc.get('name')}', not '{seen.get('name')}' that you "
                        f"looked at before. Check with the user before changing anything"
                        + ("; changes are refused until you call doc_info." if DRAWING_LOCK else "."))
            return None


LOCK = DrawingLock()


PREVIEW = "doc.preview"


def plugin_call(tool: str, args: dict, dry_run: bool = False) -> tuple[object, str | None]:
    """Call the plugin (in a worker thread) with the drawing lock. Returns the result and an optional note.
    dry_run asks the plugin for a preview: it runs the tool, reports the changes and rolls them back."""
    if dry_run:
        # An older plugin ignores dry_run and would make the change for real, so ask first, every time (Civil 3D
        # may have been restarted with another plugin version since the last call).
        ping = bridge.call("system.ping")
        if "preview" not in (ping.get("features") or [] if isinstance(ping, dict) else []):
            raise bridge.Civil3DError(
                "The Civil 3D plugin is older than this MCP server and can't preview, so nothing was run. Close "
                "Civil 3D, run install.bat again, then restart Claude.")
    try:
        result, doc = bridge.call_with_doc(tool, args, LOCK.expect(tool), dry_run)
    except bridge.Civil3DError as e:
        LOCK.update(tool, e.doc, ok=False)
        raise
    notes = [n for n in (describe_changes((doc or {}).get("changes")), LOCK.update(tool, doc, ok=True)) if n]
    return result, "\n\n".join(notes) or None


def _types(counts: dict) -> str:
    return ", ".join(f"{n} {t}" for t, n in counts.items())


def describe_changes(ch: dict | None) -> str | None:
    """One line on what the plugin measured in the drawing after a change was saved (verify after write)."""
    if not isinstance(ch, dict):
        return None                   # a read, a preview, a command-only tool, or an older plugin
    parts = [f"{label} {_types(ch[key])}" for key, label in (("added", "added"), ("changed", "changed"),
                                                             ("deleted", "deleted")) if ch.get(key)]
    if ch.get("new_handles"):
        parts[0] += f" (new handles {', '.join(ch['new_handles'])}{'...' if len(ch['new_handles']) >= 5 else ''})"
    if ch.get("settings"):
        parts.append("settings " + "; ".join(f"{k}: {v}" for k, v in ch["settings"].items()))
    text = "Checked in the drawing after saving: " + ("; ".join(parts) if parts else "nothing changed")
    if ch.get("problems"):
        text += ". WARNING: " + " ".join(ch["problems"]) + " Tell the user."
    elif not parts:
        text += ". If a change was expected, tell the user and check the inputs."
    return text


def _shrink(value, keep: int):
    if isinstance(value, list):
        items = [_shrink(v, keep) for v in value[:keep]]
        if len(value) > keep:
            items.append({"_summarized": f"{len(value) - keep} more items not shown"})
        return items
    if isinstance(value, dict):
        return {k: _shrink(v, keep) for k, v in value.items()}
    if isinstance(value, str) and len(value) > 2000:
        return value[:2000] + f"... ({len(value) - 2000} more characters)"
    return value


def fit_result(result, limit: int | None = None) -> str:
    """JSON text of a tool result, shortened to `limit` characters while keeping its shape."""
    limit = limit or MAX_RESULT_CHARS
    text = json.dumps(result, indent=1, ensure_ascii=False)
    if len(text) <= limit:
        return text
    original = len(text)
    for keep in (200, 100, 50, 20, 10, 5, 2, 1):
        text = json.dumps(_shrink(result, keep), indent=1, ensure_ascii=False)
        if len(text) <= limit:
            break
    if len(text) > limit:
        text = text[:limit] + "\n..."
    return (f"[Result shortened from {original} to {len(text)} characters. Ask for less (filters, limits, "
            f"or doc_summary) to see everything.]\n" + text)

INSTRUCTIONS = """You control Autodesk Civil 3D through these tools.
- When the user says "use Civil 3D" (or starts asking for drawing work), call system_ping, then doc_info,
  and give a 2-3 line summary of the open drawing. If ping fails, ask them to open Civil 3D and a drawing.
- On a large or unfamiliar drawing, call doc_summary before listing objects.
- "Civil 3D is busy" means a dialog box is open or the user is in a command (named in the message); nothing was
  run. Ask the user to close or finish it, then try once more.
- After a change, the reply ends with "Checked in the drawing after saving: ..." (measured in the drawing). Report
  from it; if it says nothing changed or has a WARNING, tell the user.
- To take back your own change, use doc_undo; if it refuses, the user changed the drawing since: ask them to
  press Ctrl+Z in Civil 3D.
- Before deleting or changing many objects (or running code_execute on existing objects), preview it with
  doc_preview (same tool name and args, nothing changes), tell the user the result and wait for a yes.
- "Drawing lock" (or a note that the drawing changed) means the user switched drawings: nothing was changed.
  Ask which drawing to work on before calling doc_info again; never switch on your own.
- Coordinates are drawing units; points are [x, y] or [x, y, z].
- Angles are DEGREES in dedicated tools, but RADIANS in props_set / props_call (raw API).
- Every create tool returns a handle. Civil objects can also be referred to by NAME.
- To change any property not covered by a tool: props_get (see names) then props_set.
- To use any API method: props_methods then props_call.
- Last resort: doc_run_command sends text to the command line.
- Styles live in collections: use styles_categories / styles_list / styles_get / styles_set / styles_display.
- For anything complex or not covered: skills_search → skills_read → adapt → code_query (read) / code_execute (write).
"""
SEARCH_INSTRUCTIONS = """- Only a few tools are listed. Find any other tool with tools_search (words or a group), then run it with
  tools_read (read-only tools) or tools_call (tools that add, change or delete). The tool names above
  (props_set, skills_search, code_query...) are reached the same way.
"""
if MODE == "search":
    INSTRUCTIONS += SEARCH_INSTRUCTIONS

TYPE_MAP = {
    "string": {"type": "string"},
    "number": {"type": "number"},
    "integer": {"type": "integer"},
    "boolean": {"type": "boolean"},
    "object": {"type": "object"},
    "object_or_json": {"type": ["object", "string"]},   # some clients send nested objects as JSON text
    "array": {"type": "array"},
    "point": {"type": "array", "items": {"type": "number"}, "minItems": 2, "maxItems": 3},
    "points": {"type": "array", "items": {"type": "array", "items": {"type": "number"}, "minItems": 2, "maxItems": 3}},
    "strings": {"type": "array", "items": {"type": "string"}},
    "numbers": {"type": "array", "items": {"type": "number"}},
}


def mcp_name(plugin_name: str) -> str:
    return plugin_name.replace(".", "_")


def input_schema(params: dict) -> dict:
    props, required = {}, []
    for p, spec in params.items():
        ptype, pdesc = spec[0], spec[1]
        schema = dict(TYPE_MAP[ptype])
        # 'size' can be a name or index; 'value' any scalar
        if p in ("size", "value"):
            schema = {"type": ["string", "number", "boolean"]}
        if pdesc:
            schema["description"] = pdesc
        props[p] = schema
        if len(spec) > 2 and spec[2]:
            required.append(p)
    schema = {"type": "object", "properties": props}
    if required:
        schema["required"] = required
    return schema


def build_tools() -> tuple[list[types.Tool], dict[str, str]]:
    tools, lookup = [], {}
    for name, (desc, params) in all_tools(MODE).items():
        n = mcp_name(name)
        lookup[n] = name
        tools.append(types.Tool(name=n, description=desc, inputSchema=input_schema(params),
                                annotations=types.ToolAnnotations(**hints(name))))
    return tools, lookup


TOOLS, LOOKUP = build_tools()
FULL = all_tools("full")      # every tool, reachable by name in search mode
server = Server("civil3d", instructions=INSTRUCTIONS)


def kind(name: str) -> str:
    return "read-only" if name in READ_ONLY else "adds objects" if name in ADDITIVE else "changes or deletes"


SHORT_TYPES = {"point": "[x,y] or [x,y,z]", "points": "list of [x,y(,z)]", "strings": "list of text",
               "numbers": "list of numbers", "string": "text", "object_or_json": "object"}


def short_params(params: dict) -> dict:
    """Parameters as one short line each, e.g. 'start': '[x,y] or [x,y,z], required: Start point'."""
    out = {}
    for p, spec in params.items():
        t = "text or number" if p in ("size", "value") else SHORT_TYPES.get(spec[0], spec[0])
        req = ", required" if len(spec) > 2 and spec[2] else ""
        out[p] = f"{t}{req}" + (f": {spec[1]}" if spec[1] else "")
    return out


def run_meta(tool: str, args: dict):
    """tools.search returns ("result", data); tools.read / tools.call return ("run", plugin tool, its args)."""
    if tool == "tools.search":
        limit = max(1, min(int(args.get("limit", 6)), 40))
        names, total = meta.search(FULL, args.get("query", ""), args.get("group", ""), limit)
        if not names:
            return "result", {"tools": [], "note": "Nothing matched. Try other words, or one of the groups: "
                                                   + meta.groups_text(FULL)}
        found = [{"name": mcp_name(n), "kind": kind(n), "description": FULL[n][0],
                  "parameters": short_params(FULL[n][1])} for n in names]
        out = {"tools": found, "run_with": "tools_read for read-only tools, tools_call for the others"}
        if total > len(names):
            out["more"] = f"{total - len(names)} more matched; raise limit or add words to narrow it down"
        return "result", out
    if tool == "tools.read":
        named = meta.resolve(FULL, str(args.get("name", "")))
        if named is not None and named not in READ_ONLY:
            raise ValueError(f"{mcp_name(named)} can change the drawing ({kind(named)}); run it with tools_call.")
    target, targs = prepare(args)
    return "run", target, targs


def prepare(args: dict) -> tuple[str, dict]:
    """Resolve {"name": tool, "args": {...}} (tools_read, tools_call, doc_preview) and check the parameters."""
    target = meta.resolve(FULL, str(args.get("name", "")))
    if target is None:
        close, _ = meta.search(FULL, str(args.get("name", "")).replace("_", " "), "", 5)
        raise ValueError(f"No tool named '{args.get('name')}'. Did you mean: "
                         f"{', '.join(mcp_name(n) for n in close) or 'nothing close; use tools_search'}?")
    targs = args.get("args") or {}
    if isinstance(targs, str):
        targs = json.loads(targs) if targs.strip() else {}
    if not isinstance(targs, dict):
        raise ValueError("args must be an object of parameter names and values.")
    targs = {k: v for k, v in targs.items() if v is not None}
    schema = input_schema(FULL[target][1])
    missing = [p for p in schema.get("required", []) if p not in targs]
    unknown = [p for p in targs if p not in schema["properties"]]
    if missing or unknown:
        problems = ([f"missing {', '.join(missing)}"] if missing else []) + \
                   ([f"unknown {', '.join(unknown)}"] if unknown else [])
        raise ValueError(f"{mcp_name(target)}: {'; '.join(problems)}. Parameters: "
                         f"{json.dumps(short_params(FULL[target][1]), ensure_ascii=False)}")
    return target, targs


def preview_target(args: dict) -> tuple[str, dict]:
    """The tool doc_preview should dry-run, after checking it can be previewed at all."""
    target, targs = prepare(args)
    if target in LOCAL or target in META or target == PREVIEW:
        raise ValueError(f"{mcp_name(target)} runs in the MCP server, not in Civil 3D; there is nothing to preview.")
    if target in READ_ONLY:
        raise ValueError(f"{mcp_name(target)} only reads the drawing; run it directly, no preview needed.")
    return target, targs


@server.list_tools()
async def list_tools() -> list[types.Tool]:
    return TOOLS


@server.call_tool()
async def call_tool(name: str, arguments: dict | None) -> list[types.TextContent]:
    plugin_tool = LOOKUP.get(name)
    if plugin_tool is None:
        raise ValueError(f"Unknown tool {name}")
    args = {k: v for k, v in (arguments or {}).items() if v is not None}
    if plugin_tool in META:
        try:
            step = run_meta(plugin_tool, args)
        except (ValueError, TypeError) as e:
            return [types.TextContent(type="text", text=f"ERROR: {e}")]
        if step[0] == "result":
            return [types.TextContent(type="text", text=fit_result(step[1]))]
        plugin_tool, args = step[1], step[2]
    dry_run = False
    if plugin_tool == PREVIEW:
        try:
            plugin_tool, args = preview_target(args)
        except (ValueError, TypeError) as e:
            return [types.TextContent(type="text", text=f"ERROR: {e}")]
        dry_run = True
    note = None
    try:
        if plugin_tool in LOCAL:
            result = skills_lib.call(plugin_tool, args)
            if isinstance(result, str):
                return [types.TextContent(type="text", text=result)]
        else:
            result, note = await anyio.to_thread.run_sync(plugin_call, plugin_tool, args, dry_run)
    except (bridge.Civil3DError, KeyError) as e:
        return [types.TextContent(type="text", text=f"ERROR: {e}")]
    text = fit_result(result)
    return [types.TextContent(type="text", text=f"{text}\n\n{note}" if note else text)]


async def main() -> None:
    async with stdio_server() as (read, write):
        await server.run(read, write, server.create_initialization_options())


if __name__ == "__main__":
    asyncio.run(main())

"""Civil 3D MCP server.

Claude Desktop starts this program. It lists the tools to Claude and forwards every
call to the Civil 3D plugin over 127.0.0.1:8765 (see bridge.py).
"""
from __future__ import annotations

import asyncio
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import anyio  # noqa: E402
import mcp.types as types  # noqa: E402
from mcp.server.lowlevel import Server  # noqa: E402
from mcp.server.stdio import stdio_server  # noqa: E402

import os  # noqa: E402

import bridge  # noqa: E402
import skills_lib  # noqa: E402
from tools import LOCAL, all_tools, hints  # noqa: E402

MODE = os.environ.get("CIVIL3D_MCP_MODE", "full").lower()  # full | lean | code
# Results longer than this are shortened (long lists cut to a prefix) so a big drawing can't flood the chat.
MAX_RESULT_CHARS = int(os.environ.get("CIVIL3D_MCP_MAX_RESULT_CHARS", "60000"))


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
- Coordinates are drawing units; points are [x, y] or [x, y, z].
- Angles are DEGREES in dedicated tools, but RADIANS in props_set / props_call (raw API).
- Every create tool returns a handle. Civil objects can also be referred to by NAME.
- To change any property not covered by a tool: props_get (see names) then props_set.
- To use any API method: props_methods then props_call.
- Last resort: doc_run_command sends text to the command line.
- Styles live in collections: use styles_categories / styles_list / styles_get / styles_set / styles_display.
- For anything complex or not covered: skills_search → skills_read → adapt → code_query (read) / code_execute (write).
"""

TYPE_MAP = {
    "string": {"type": "string"},
    "number": {"type": "number"},
    "integer": {"type": "integer"},
    "boolean": {"type": "boolean"},
    "object": {"type": "object"},
    "array": {"type": "array"},
    "point": {"type": "array", "items": {"type": "number"}, "minItems": 2, "maxItems": 3},
    "points": {"type": "array", "items": {"type": "array", "items": {"type": "number"}, "minItems": 2, "maxItems": 3}},
    "strings": {"type": "array", "items": {"type": "string"}},
    "numbers": {"type": "array", "items": {"type": "number"}},
}


def mcp_name(plugin_name: str) -> str:
    return plugin_name.replace(".", "_")


def build_tools() -> tuple[list[types.Tool], dict[str, str]]:
    tools, lookup = [], {}
    for name, (desc, params) in all_tools(MODE).items():
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
        n = mcp_name(name)
        lookup[n] = name
        tools.append(types.Tool(name=n, description=desc, inputSchema=schema,
                                annotations=types.ToolAnnotations(**hints(name))))
    return tools, lookup


TOOLS, LOOKUP = build_tools()
server = Server("civil3d", instructions=INSTRUCTIONS)


@server.list_tools()
async def list_tools() -> list[types.Tool]:
    return TOOLS


@server.call_tool()
async def call_tool(name: str, arguments: dict | None) -> list[types.TextContent]:
    plugin_tool = LOOKUP.get(name)
    if plugin_tool is None:
        raise ValueError(f"Unknown tool {name}")
    args = {k: v for k, v in (arguments or {}).items() if v is not None}
    try:
        if plugin_tool in LOCAL:
            result = skills_lib.call(plugin_tool, args)
            if isinstance(result, str):
                return [types.TextContent(type="text", text=result)]
        else:
            result = await anyio.to_thread.run_sync(bridge.call, plugin_tool, args)
    except (bridge.Civil3DError, KeyError) as e:
        return [types.TextContent(type="text", text=f"ERROR: {e}")]
    return [types.TextContent(type="text", text=fit_result(result))]


async def main() -> None:
    async with stdio_server() as (read, write):
        await server.run(read, write, server.create_initialization_options())


if __name__ == "__main__":
    asyncio.run(main())

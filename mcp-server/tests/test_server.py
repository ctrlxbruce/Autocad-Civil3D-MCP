"""Run:  python -m pytest tests   (or just: python tests/test_server.py)

1. Every MCP tool maps to a [Tool("...")] in the C# plugin (and vice versa).
2. End-to-end: start server.py over stdio, call a tool, and check it reaches a fake plugin.
"""
import json
import os
import re
import socket
import sys
import threading
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT))

from tools import ADDITIVE, LOCAL, READ_ONLY, all_tools, hints  # noqa: E402


def cs_tools() -> set:
    names = set()
    for f in (ROOT.parent / "civil3d-plugin").rglob("*.cs"):
        names |= set(re.findall(r'(?m)^\s*\[Tool\("([a-z0-9_.]+)"', f.read_text(encoding="utf-8")))
    return names | {"system.ping", "system.list_tools"}


def test_catalogue_matches_plugin():
    py = set(all_tools()) - LOCAL
    cs = cs_tools()
    assert py - cs == set(), f"in Python but not C#: {sorted(py - cs)}"
    assert cs - py == set(), f"in C# but not Python: {sorted(cs - py)}"


def test_read_only_matches_plugin():
    """The plugin aborts the transaction of ReadOnly tools (no empty undo step); Python labels them read-only."""
    cs = set()
    for f in (ROOT.parent / "civil3d-plugin").rglob("*.cs"):
        cs |= set(re.findall(r'\[Tool\("([a-z0-9_.]+)"[^\]]*ReadOnly = true', f.read_text(encoding="utf-8")))
    py = {n for n in READ_ONLY if n not in LOCAL and not n.startswith("system.")}
    assert cs == py, f"ReadOnly only in C#: {sorted(cs - py)}; only in Python: {sorted(py - cs)}"


def fake_plugin(port, seen, state=None):
    """Answers every request. With state={"doc": {"id", "name"}} it also names the active drawing and refuses
    requests whose expect_doc is another drawing, like the real plugin's drawing lock."""
    srv = socket.socket()
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind(("127.0.0.1", port))
    srv.listen()

    def loop():
        while True:
            conn, _ = srv.accept()
            with conn:
                data = b""
                while not data.endswith(b"\n"):
                    data += conn.recv(65536)
                req = json.loads(data)
                seen.append(req)
                reply = {"id": req["id"], "ok": True, "result": {"handle": "2A3", "echo": req["tool"]}}
                if req["tool"] == "system.ping":
                    old = bool(state and state.get("old_plugin"))
                    reply["result"] = {"pong": True, **({} if old else {"features": ["drawing_lock", "preview"]})}
                if state:
                    doc = state["doc"]
                    want = req.get("expect_doc")
                    if want and want["id"] != doc["id"]:
                        reply = {"id": req["id"], "ok": False, "error": "Drawing lock: nothing was changed."}
                    reply["doc"] = dict(doc)
                conn.sendall((json.dumps(reply) + "\n").encode())

    threading.Thread(target=loop, daemon=True).start()


def test_annotations():
    names = set(all_tools())
    assert READ_ONLY <= names, f"unknown READ_ONLY names: {sorted(READ_ONLY - names)}"
    assert ADDITIVE <= names, f"unknown ADDITIVE names: {sorted(ADDITIVE - names)}"
    assert not READ_ONLY & ADDITIVE
    assert hints("system.ping") == {"readOnlyHint": True, "openWorldHint": False}
    assert hints("drafting.line")["destructiveHint"] is False
    for n in ("entity.delete", "layers.delete", "code.execute", "doc.run_command", "doc.save", "props.set"):
        assert hints(n)["destructiveHint"] is True, n
    assert "openWorldHint" not in hints("code.execute")


def test_modes():
    assert len(all_tools("code")) == 8
    assert len(all_tools("code")) < len(all_tools("lean")) < len(all_tools("full"))
    search = all_tools("search")
    assert len(search) == 10 and {"tools.search", "tools.read", "tools.call", "doc.preview"} <= set(search)
    assert "{groups}" not in search["tools.search"][0] and "pipes (10)" in search["tools.search"][0]
    assert not {"tools.search", "tools.read", "tools.call"} & set(all_tools("full"))


def test_search_ranking():
    from tools import meta
    full = all_tools("full")
    top = {q: meta.search(full, q)[0][0] for q in
           ["pipe slope", "dimension radius", "import points csv", "manning capacity", "rainfall runoff",
            "plot pdf", "water main", "rename layer", "hatch", "cost estimate"]}
    assert top == {"pipe slope": "pipes.add_pipe", "dimension radius": "dim.radius",
                   "import points csv": "points.import", "manning capacity": "pipes.hydraulics",
                   "rainfall runoff": "hydrology.rational", "plot pdf": "layouts.plot_pdf",
                   "water main": "pressure.network_create", "rename layer": "layers.set", "hatch": "drafting.hatch",
                   "cost estimate": "qty.cost_estimate"}, top
    assert len(meta.search(full, "", "pipes", 40)[0]) == 10
    assert meta.resolve(full, "mcp__civil3d__pipes_add_pipe") == "pipes.add_pipe"
    assert meta.resolve(full, "points.import") == "points.import" and meta.resolve(full, "nope") is None


def test_result_trimming():
    import server
    small = {"a": [1, 2, 3]}
    assert server.fit_result(small, 1000) == json.dumps(small, indent=1)
    big = {"items": [{"i": i, "text": "x" * 40} for i in range(5000)], "name": "big"}
    text = server.fit_result(big, 5000)
    assert len(text) < 5300 and "_summarized" in text and "Result shortened" in text
    assert '"name": "big"' in text          # the shape (other keys) is kept


def test_end_to_end():
    import anyio
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    import tempfile
    port = 18765
    seen = []
    fake_plugin(port, seen)
    token_file = Path(tempfile.mkdtemp()) / f"token-{port}"
    token_file.write_text("ABC123", encoding="ascii")      # what the plugin writes when it starts

    async def run():
        params = StdioServerParameters(command=sys.executable, args=[str(ROOT / "server.py")],
                                       env={**os.environ, "CIVIL3D_MCP_PORT": str(port),
                                            "CIVIL3D_MCP_TOKEN_FILE": str(token_file)})
        async with stdio_client(params) as (r, w):
            async with ClientSession(r, w) as s:
                await s.initialize()
                tools = (await s.list_tools()).tools
                assert len(tools) == len(all_tools())
                assert all(re.fullmatch(r"[a-zA-Z0-9_-]{1,64}", t.name) for t in tools)
                by_name = {t.name: t for t in tools}
                assert by_name["system_ping"].annotations.readOnlyHint is True
                assert by_name["entity_delete"].annotations.destructiveHint is True
                res = await s.call_tool("drafting_line", {"start": [0, 0], "end": [10, 5], "layer": "C-ROAD"})
                return len(tools), res.content[0].text

    n, text = anyio.run(run)
    import skills_lib
    assert skills_lib.search("contour smoothing")[0]["name"] == "contour_style"
    assert "csharp" in skills_lib.read("surfaces/contour_style")
    assert '"2A3"' in text
    assert seen[-1]["tool"] == "drafting.line"
    assert seen[-1]["args"]["layer"] == "C-ROAD"
    assert seen[-1]["token"] == "ABC123"              # every request carries the plugin's token
    return n


def test_search_mode_end_to_end():
    import anyio
    import tempfile
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    port = 18766
    seen = []
    fake_plugin(port, seen)
    token_file = Path(tempfile.mkdtemp()) / f"token-{port}"
    token_file.write_text("XYZ", encoding="ascii")

    async def run():
        params = StdioServerParameters(command=sys.executable, args=[str(ROOT / "server.py")],
                                       env={**os.environ, "CIVIL3D_MCP_PORT": str(port), "CIVIL3D_MCP_MODE": "search",
                                            "CIVIL3D_MCP_TOKEN_FILE": str(token_file)})
        async with stdio_client(params) as (r, w):
            async with ClientSession(r, w) as s:
                init = await s.initialize()
                tools = {t.name: t for t in (await s.list_tools()).tools}
                assert len(tools) == 10 and "drafting_line" not in tools
                assert tools["tools_read"].annotations.readOnlyHint is True
                assert tools["tools_call"].annotations.destructiveHint is True
                assert "tools_search" in (init.instructions or "")

                async def text(tool, args):
                    return (await s.call_tool(tool, args)).content[0].text

                found = json.loads(await text("tools_search", {"query": "draw line"}))
                assert found["tools"][0]["name"] == "drafting_line"
                assert found["tools"][0]["kind"] == "adds objects"
                assert found["tools"][0]["parameters"]["start"].startswith("[x,y] or [x,y,z], required")
                assert "required" not in found["tools"][0]["parameters"].get("layer", "")

                assert "run it with tools_call" in await text("tools_read", {"name": "drafting_line", "args": {}})
                assert "missing end" in await text("tools_call", {"name": "drafting_line", "args": {"start": [0, 0]}})
                assert "unknown colour" in await text(
                    "tools_call", {"name": "drafting_line", "args": {"start": [0, 0], "end": [1, 1], "colour": 1}})
                assert "Did you mean" in await text("tools_call", {"name": "draw_line_please", "args": {}})
                assert len(seen) == 0                                  # nothing invalid reached the plugin

                assert '"2A3"' in await text("tools_call", {"name": "drafting_line",
                                                            "args": {"start": [0, 0], "end": [3, 4]}})
                assert seen[-1]["tool"] == "drafting.line" and seen[-1]["args"]["end"] == [3, 4]
                r = await text("tools_read", {"name": "pipes.network_info", "args": '{"network": "SEWER"}'})
                assert seen[-1]["tool"] == "pipes.network_info" and seen[-1]["args"] == {"network": "SEWER"}, r
                assert "csharp" in await text("tools_read", {"name": "skills_read",
                                                             "args": {"name": "surfaces/contour_style"}})
                assert seen[-1]["token"] == "XYZ"

    anyio.run(run)


def test_drawing_lock_end_to_end():
    import anyio
    import tempfile
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    port = 18767
    seen = []
    state = {"doc": {"id": "aaa", "name": "Site.dwg"}}
    fake_plugin(port, seen, state)
    token_file = Path(tempfile.mkdtemp()) / f"token-{port}"
    token_file.write_text("T", encoding="ascii")

    async def run():
        params = StdioServerParameters(command=sys.executable, args=[str(ROOT / "server.py")],
                                       env={**os.environ, "CIVIL3D_MCP_PORT": str(port),
                                            "CIVIL3D_MCP_TOKEN_FILE": str(token_file)})
        async with stdio_client(params) as (r, w):
            async with ClientSession(r, w) as s:
                await s.initialize()

                async def text(tool, args=None):
                    return (await s.call_tool(tool, args or {})).content[0].text

                line = {"start": [0, 0], "end": [1, 1]}
                await text("doc_info")                                       # Claude looks at Site.dwg
                await text("drafting_line", line)
                assert seen[-1]["expect_doc"] == {"id": "aaa", "name": "Site.dwg"}
                await text("query_list")
                assert "expect_doc" not in seen[-1]                          # reads are never locked

                state["doc"] = {"id": "bbb", "name": "Other.dwg"}            # the user switches drawings
                t = await text("query_list")
                assert "now 'Other.dwg', not 'Site.dwg'" in t and "doc_info" in t
                n = len(seen)
                t = await text("drafting_line", line)
                assert t.startswith("ERROR: Drawing lock") and seen[-1]["expect_doc"]["id"] == "aaa" and len(seen) == n + 1
                t = await text("drafting_line", line)                        # still refused, no silent switch
                assert t.startswith("ERROR: Drawing lock")

                t = await text("doc_info")                                   # Claude looks again: lock moves
                assert "different drawing from 'Site.dwg'" in t
                t = await text("drafting_line", line)
                assert '"2A3"' in t and seen[-1]["expect_doc"]["id"] == "bbb"
                assert "dry_run" not in seen[-1]                             # normal calls are real

                # Preview: the target tool goes to the plugin with dry_run, under the same drawing lock.
                n = len(seen)
                await text("doc_preview", {"name": "entity_delete", "args": {"handles": ["2A3"]}})
                assert seen[-1]["tool"] == "entity.delete" and seen[-1]["dry_run"] is True
                assert seen[-1]["args"] == {"handles": ["2A3"]} and seen[-1]["expect_doc"]["id"] == "bbb"
                assert "only reads" in await text("doc_preview", {"name": "query_list"})
                assert "nothing to preview" in await text("doc_preview", {"name": "skills_read",
                                                                          "args": {"name": "x"}})
                assert "missing handles" in await text("doc_preview", {"name": "entity_delete"})
                assert len(seen) == n + 2                                    # ping + preview; refused ones never sent

                state["old_plugin"] = True                                   # a plugin without preview support
                n = len(seen)
                t = await text("doc_preview", {"name": "entity_delete", "args": {"handles": ["2A3"]}})
                assert "older than this MCP server" in t and "nothing was run" in t
                assert [r["tool"] for r in seen[n:]] == ["system.ping"]      # the delete itself was never sent

    anyio.run(run)


def test_drawing_lock_unit():
    import server
    lock = server.DrawingLock()
    a, b = {"id": "1", "name": "A.dwg"}, {"id": "2", "name": "B.dwg"}
    assert lock.expect("drafting.line") is None                              # nothing seen yet: no check
    assert lock.update("system.ping", a, ok=True) is None                    # first reply sets the lock
    assert lock.expect("drafting.line") == a and lock.expect("query.list") is None
    assert lock.update("query.list", b, ok=True).startswith("Note: the active drawing is now 'B.dwg'")
    assert lock.expect("code.execute") == a                                  # a read doesn't move the lock
    assert lock.update("drafting.line", b, ok=False) is None                 # the refusal speaks for itself
    assert "different drawing" in lock.update("doc.info", b, ok=True)
    assert lock.expect("entity.delete") == b
    assert lock.update("doc.summary", None, ok=True) is None                 # an old plugin sends no doc


if __name__ == "__main__":
    test_catalogue_matches_plugin()
    test_annotations()
    test_read_only_matches_plugin()
    test_modes()
    test_search_ranking()
    test_result_trimming()
    n = test_end_to_end()
    test_search_mode_end_to_end()
    test_drawing_lock_unit()
    test_drawing_lock_end_to_end()
    print(f"OK: {n} tools, catalogue matches plugin, end-to-end call works, search mode and drawing lock work.")

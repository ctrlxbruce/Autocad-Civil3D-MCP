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


def fake_plugin(port, seen):
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


if __name__ == "__main__":
    test_catalogue_matches_plugin()
    test_annotations()
    test_modes()
    test_result_trimming()
    n = test_end_to_end()
    print(f"OK: {n} tools, catalogue matches plugin, end-to-end call works.")

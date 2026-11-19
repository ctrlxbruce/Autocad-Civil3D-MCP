"""Sends one request to the Civil 3D plugin and waits for the reply.

The plugin listens on 127.0.0.1:8765 (change with env var CIVIL3D_MCP_PORT).
Protocol: one JSON object per line, both directions. Every request carries the token that the plugin
writes to %LOCALAPPDATA%\\Civil3DMcp\\token-<port> each time it starts; only this Windows user can read it.
"""
from __future__ import annotations

import itertools
import json
import os
import socket
from pathlib import Path

HOST = os.environ.get("CIVIL3D_MCP_HOST", "127.0.0.1")
PORT = int(os.environ.get("CIVIL3D_MCP_PORT", "8765"))
TIMEOUT = float(os.environ.get("CIVIL3D_MCP_TIMEOUT", "300"))

_ids = itertools.count(1)


class Civil3DError(RuntimeError):
    def __init__(self, message: str, doc: dict | None = None):
        super().__init__(message)
        self.doc = doc          # the drawing that was active when the plugin answered, if it said


def token_file() -> Path:
    if os.environ.get("CIVIL3D_MCP_TOKEN_FILE"):
        return Path(os.environ["CIVIL3D_MCP_TOKEN_FILE"])
    base = os.environ.get("LOCALAPPDATA") or str(Path.home() / "AppData" / "Local")
    return Path(base) / "Civil3DMcp" / f"token-{PORT}"


def read_token() -> str:
    """Read on every call: Civil 3D makes a new token each time the plugin starts."""
    try:
        return token_file().read_text(encoding="ascii").strip()
    except OSError:
        return ""


def call(tool: str, args: dict | None = None) -> object:
    return call_with_doc(tool, args)[0]


def call_with_doc(tool: str, args: dict | None = None, expect_doc: dict | None = None,
                  dry_run: bool = False) -> tuple[object, dict | None]:
    """Result plus the active drawing ({"id", "name"}). With expect_doc, the plugin refuses to run the tool when
    another drawing is active (drawing lock). With dry_run, it previews the tool and rolls everything back."""
    req = {"id": next(_ids), "token": read_token(), "tool": tool, "args": args or {}}
    if expect_doc:
        req["expect_doc"] = expect_doc
    if dry_run:
        req["dry_run"] = True
    try:
        with socket.create_connection((HOST, PORT), timeout=TIMEOUT) as s:
            s.sendall((json.dumps(req) + "\n").encode("utf-8"))
            buf = b""
            while not buf.endswith(b"\n"):
                chunk = s.recv(65536)
                if not chunk:
                    break
                buf += chunk
    except ConnectionRefusedError as e:
        raise Civil3DError(
            "Civil 3D is not reachable. Open Civil 3D, open a drawing, and load the plugin "
            "(NETLOAD Civil3DMcp.dll, or install the autoloader bundle). Type MCPSTATUS in Civil 3D to check."
        ) from e
    except socket.timeout as e:
        raise Civil3DError("Civil 3D did not answer in time (is a dialog box open?).") from e

    if not buf:
        raise Civil3DError("Empty reply from Civil 3D.")
    reply = json.loads(buf.decode("utf-8"))
    doc = reply.get("doc") if isinstance(reply.get("doc"), dict) else None
    if not reply.get("ok"):
        raise Civil3DError(reply.get("error", "Unknown error"), doc)
    return reply.get("result"), doc

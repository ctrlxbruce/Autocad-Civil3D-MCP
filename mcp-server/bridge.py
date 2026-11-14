"""Sends one request to the Civil 3D plugin and waits for the reply.

The plugin listens on 127.0.0.1:8765 (change with env var CIVIL3D_MCP_PORT).
Protocol: one JSON object per line, both directions.
"""
from __future__ import annotations

import itertools
import json
import os
import socket

HOST = os.environ.get("CIVIL3D_MCP_HOST", "127.0.0.1")
PORT = int(os.environ.get("CIVIL3D_MCP_PORT", "8765"))
TIMEOUT = float(os.environ.get("CIVIL3D_MCP_TIMEOUT", "300"))

_ids = itertools.count(1)


class Civil3DError(RuntimeError):
    pass


def call(tool: str, args: dict | None = None) -> object:
    req = {"id": next(_ids), "tool": tool, "args": args or {}}
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
    if not reply.get("ok"):
        raise Civil3DError(reply.get("error", "Unknown error"))
    return reply.get("result")

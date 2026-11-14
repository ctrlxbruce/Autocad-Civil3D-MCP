---
name: civil3d
description: Work side by side with Autodesk Civil 3D through the civil3d MCP server - draft, annotate, import COGO points, build surfaces, alignments, profiles, corridors and pipe networks, and plot sheets in the drawing that is open in Civil 3D. Use when the user says "use Civil 3D" or "/civil3d", or asks for anything to be drawn, changed, measured or reported in their Civil 3D or AutoCAD drawing.
---

<!-- Installed by Civil 3D MCP (https://github.com/ctrlxbruce/Autocad-Civil3D-MCP). The uninstaller looks for this line. -->

# Civil 3D, side by side

You control the user's open Civil 3D drawing through the tools of the `civil3d` MCP server
(in Claude Code their names start with `mcp__civil3d__`, for example `mcp__civil3d__system_ping`).
The user watches Civil 3D next to this chat, so every change shows up in their drawing right away.

## Start

1. Call `system_ping`.
   - If it says **Civil 3D is not reachable**: ask the user to open Civil 3D **and a drawing**, then type
     `MCPSTATUS` on the Civil 3D command line. It should say RUNNING; if it says STOPPED, they type `MCPSTART`.
     Wait for them, then ping again.
   - If there are **no civil3d tools at all**: the server is not connected. Tell the user to run `install.bat`
     in the project folder (or say "Install this repo: https://github.com/ctrlxbruce/Autocad-Civil3D-MCP"),
     then start a new Claude session.
2. Call `doc_info` (and `civil_list` if useful) and give a 2-3 line summary: drawing name, units, and the
   Civil objects it holds (surfaces, alignments, point groups, pipe networks).
3. If the user has not said what they want yet, ask.

## While working

- Prefer a ready-made tool. If none fits: `skills_search` -> `skills_read` -> adapt the template ->
  `code_query` (read-only, always rolls back) or `code_execute` (changes the drawing).
- When unsure what is in the drawing, look first with `query_list`, `props_get` or `code_query`.
- Coordinates are drawing units; points are `[x, y]` or `[x, y, z]`. Angles are degrees in the dedicated
  tools but radians in `props_set` / `props_call`.
- Refer to Civil objects by name (surface "EG", alignment "CL-1") or by the handle a create tool returned.
- Before deleting or changing many objects, say what will change and wait for a yes.
- After drawing something, call `doc_zoom_extents` so the user can see it.
- Report briefly: what was created or changed, names or handles, and key numbers (lengths, areas, volumes).

## When a tool fails

- **"did not answer in time"**: a dialog box is probably open in Civil 3D. Ask the user to close it.
- **Pipe or structure errors about the catalog**: ask the user to set it on the Civil 3D ribbon:
  Manage tab -> Set Pipe Network Catalog.
- Otherwise read the error, fix the inputs and try once more. If it still fails, explain the problem plainly
  and suggest what the user can do in Civil 3D.

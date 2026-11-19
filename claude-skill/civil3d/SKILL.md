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

- If only a few civil3d tools are listed (search mode), find the others with `tools_search` and run them with
  `tools_read` (read-only) or `tools_call` (changes the drawing). Every tool named below works that way.
- Prefer a ready-made tool. If none fits: `skills_search` -> `skills_read` -> adapt the template ->
  `code_query` (read-only, always rolls back) or `code_execute` (changes the drawing).
- When unsure what is in the drawing, look first with `query_list`, `props_get` or `code_query`.
- Coordinates are drawing units; points are `[x, y]` or `[x, y, z]`. Angles are degrees in the dedicated
  tools but radians in `props_set` / `props_call`.
- Refer to Civil objects by name (surface "EG", alignment "CL-1") or by the handle a create tool returned.
- Pipe networks: create the network with a reference `surface` so rims follow the ground, use a parts list with
  sizes (drawings from the `_Autodesk Civil 3D (Metric) NCS.dwt` template have "Storm Sewer" and "Sanitary Sewer"),
  and give each pipe a `start_invert` plus a negative `slope` for a falling sewer (-0.01 = 1 %). Check `warnings`
  in the reply. Then `pipes_hydraulics` (design flows in L/s) and `pipes_qc` check capacity, velocity, d/D, cover
  and sumps; tell the user which criteria you used and that they should match their design standard.
- Large or unfamiliar drawing: start with `doc_summary`, then narrow down with `query_list` (layer or type filter).
- Storm runoff: `hydrology_flow_path` -> `hydrology_time_of_concentration` (path) -> `hydrology_rational`
  (intensity for that duration, from the user's IDF data). Never invent rainfall intensity; ask for it.
- Roads: assemblies get real cross sections with `assembly_add_subassembly` (lanes first, then curbs / shoulders
  hooked to the lane edge point code, e.g. ETW); check designs with `qc_profile_k_values` and `qc_alignment`.
- Quantities: `qty_pipe_lengths`, `qty_earthwork`, `qty_cost_estimate` (ask the user for the rates).
- Pressure (water) networks need a pressure parts list with parts; if Civil 3D refuses to add a part, pass on the
  tool's explanation rather than retrying.
- Before deleting or changing many objects, or running a `code_execute` script that edits existing objects, run
  it through `doc_preview` first (same name and args; nothing changes), tell the user what it would add, change
  and delete, and wait for a yes.
- After drawing something, call `doc_zoom_extents` so the user can see it.
- "Undo that": `doc_undo` (with `steps` for more than one) takes back your own last changes. If it refuses, the
  user changed the drawing since; tell them to press Ctrl+Z in Civil 3D themselves. Then check the drawing.
- Report briefly: what was created or changed, names or handles, and key numbers (lengths, areas, volumes).

## When a tool fails

- **"did not answer in time"**: a dialog box is probably open in Civil 3D. Ask the user to close it.
- **"Missing or wrong token"**: the plugin and the MCP server don't match (usually right after an update). Ask the
  user to close Civil 3D, run `install.bat` again, then restart Claude and open Civil 3D.
- **"Drawing lock"** (or a note that the active drawing changed): the user switched drawings since you last called
  `doc_info`. Nothing was changed. Tell them, ask which drawing to work on, and only then call `doc_info` (it
  moves the lock to the active drawing) and try again. Never switch on your own.
- **"Blocked for safety"**: the plugin refused a command, system variable or script on purpose. Don't look for a way
  around it; tell the user which step to do by hand in Civil 3D.
- **Pipe or structure errors about the catalog**: ask the user to set it on the Civil 3D ribbon:
  Home tab -> Create Design -> Pipe Network (drop-down) -> Set Pipe Network Catalog (command `AECCSETNETWORKCATALOG`; `doc_run_command` with `_.AECCSETNETWORKCATALOG ` opens the dialog).
  Catalog folder for Civil 3D 2025: `C:\ProgramData\Autodesk\C3D 2025\enu\Pipes Catalog`; pick Metric Pipes and
  Metric Structures for metric drawings.
- Otherwise read the error, fix the inputs and try once more. If it still fails, explain the problem plainly
  and suggest what the user can do in Civil 3D.

# Civil 3D MCP Server: Tools + Code Execution

This MCP server lets AI assistants (Claude Desktop, Claude Code, Cline ...) **draft and design inside Autodesk Civil 3D**.
It works in two ways, and you can use both at the same time:

- **157 ready-made tools.** These cover drafting, annotation, dimensions, styles, COGO points, surfaces, alignments, profiles,
  corridors, sections, pipe networks and plotting. They're fast and reliable for everyday work.
- **Code execution.** The AI writes C# that runs *inside* Civil 3D with the **full .NET API**, starting from a
  **skills library** of tested templates. Anything the tools don't cover can still be done this way.

You choose how many tools load (**modes**), so you control how many tokens each chat costs.

---

## Architecture

```
┌──────────────────────┐   stdio   ┌───────────────────────────┐  TCP / JSON lines  ┌──────────────────────────────┐
│  AI Assistant        │ ────────▶ │  MCP Server (Python)      │ ─────────────────▶ │  Civil 3D Plugin (.NET 8)    │
│  Claude Desktop,     │ ◀──────── │  157 tools · 3 modes      │ ◀───────────────── │  152 handlers + Roslyn engine │
│  Claude Code, Cline  │           │  127.0.0.1:8765           │                    │  runs on Civil 3D main thread │
└──────────────────────┘           └─────────────┬─────────────┘                    └──────────────┬───────────────┘
                                                 │                                                 │
                                    ┌────────────▼────────────┐                     ┌──────────────▼──────────────┐
                                    │  Skills Library         │                     │  Full AutoCAD + Civil 3D    │
                                    │  skills/**/*.skill.md   │                     │  .NET API, one transaction  │
                                    └─────────────────────────┘                     │  per call (undo-safe)       │
                                                                                    └─────────────────────────────┘
```

---

## Modes (token control)

Set `CIVIL3D_MCP_MODE` in the Claude config. The tool list is sent at the start of **every** chat, so fewer tools means fewer tokens.

| Mode | Tools | Tool-list size | Best for |
|---|---|---|---|
| `full` (default) | 157 | ~22,600 tokens | Everyday drafting: Claude picks a ready tool, so calls are short and rarely fail |
| `lean` | 32 | ~4,800 tokens | Common drafting tools + generic property tools + code + skills |
| `code` | 8 | ~1,000 tokens | Like a code-only server: Claude writes C# for everything, using the skills library |

---

## Tools at a glance

| Group | Tools | Examples |
|---|---|---|
| **Code & skills** | 5 | `code_execute` ⚠️ changes the drawing · `code_query` ✅ read-only · `skills_list` · `skills_search` · `skills_read` |
| **Core & properties** | 17 | `doc_info`, `query_list`, `props_get` / `props_set` (any property of anything), `props_call` (any API method), `doc_run_command` |
| **Drafting** | 14 | line, polyline, 3D polyline, rectangle, polygon, circle, arc, ellipse, spline, point, hatch, xline, ray, donut |
| **Modify** | 12 | move, copy, rotate, scale, mirror, offset, explode, join, rectangular/polar array, match properties, vertex edit |
| **Layers** | 6 | create/edit layers, current layer, delete, load linetypes |
| **Annotation & styles** | 20 | text, mtext, table, mleader · text / dimension / mleader styles · linear, aligned, angular, radius, diameter, arc length, ordinate, polyline-segment dims |
| **Blocks** | 7 | define, insert with attributes, dynamic properties, import DWG, xref |
| **Layouts** | 7 | layouts, viewports, paper sizes, **plot to PDF** |
| **COGO points** | 11 | create, list, edit, delete, **import CSV/TXT**, elevations from surface, point groups, point styles, description keys |
| **Surfaces** | 15 | create TIN/grid, point groups, points, breaklines, contours, boundaries, DEM, `.tin` import, edit, **contour style + smoothing**, extract contours, analysis, **cut/fill volume** |
| **Alignments & profiles** | 15 | from polyline / PIs (curves + spirals), offset, station/offset, equations, design speeds, EG profile, FG by PVIs, profile views |
| **Corridors & sections** | 11 | assembly, corridor, corridor surface, sample lines, section views, feature lines, sites, parcels |
| **Pipes** | 9 | parts lists, network, structures, pipes, connect, rules, report, pressure network info |
| **Civil styles** | 8 | list Civil objects, every style collection: list / create / get / set / **display components** |

Full list with inputs: [`docs/tool-reference.md`](docs/tool-reference.md)

---

### How code execution works

1. Claude searches the skills library (`skills_search "contour smoothing"`).
2. Claude reads the template (`skills_read`) and adapts it to your drawing.
3. Claude sends it with `code_query` (read-only) or `code_execute` (changes the drawing).
4. The Roslyn engine compiles and runs it **inside Civil 3D**, on the main thread, inside an open transaction.
5. The script's `return` value comes back as JSON.

### Example interaction

**You:** *"What surfaces are in my drawing?"*

**Claude calls `code_query`:**
```csharp
var list = new List<object>();
foreach (ObjectId id in CivilDoc.GetSurfaceIds())
{
    var s = (Surface)Transaction.GetObject(id, OpenMode.ForRead);
    var p = s.GetGeneralProperties();
    list.Add(new { s.Name, p.NumberOfPoints, p.MinimumElevation, p.MaximumElevation });
}
return list;
```
**Result:**
```json
{ "result": [ { "Name": "EG", "NumberOfPoints": 18452, "MinimumElevation": 212.4, "MaximumElevation": 248.9 } ], "log": [] }
```

Or, with ready-made tools: *"Draw a 20 × 10 rectangle on layer C-BLDG"* → `drafting_rectangle {corner1:[0,0], corner2:[20,10], layer:"C-BLDG"}`.

---

## Skills library

```
skills/
├── surfaces/    list_surfaces · create_from_point_group · contour_style · cut_fill_volume · add_boundary
├── points/      import_csv · summary_by_description · point_group_by_description
├── alignments/  from_polyline · geometry_report · eg_profile_and_view
├── drawing/     layer_standard · label_polyline_lengths · count_by_layer
├── geometry/    offset_both_sides
├── pipes/       network_report
└── workflows/   eg_from_csv · road_quick_start
```

Each `*.skill.md` file has a short header (name, category, description, tags, mode), example inputs and the C# code.
To add your own, drop a new `.skill.md` file in a folder. No rebuild is needed.

### Script globals

| Global | Type | Description |
|---|---|---|
| `Document` | `Document` | Active drawing |
| `CivilDoc` | `CivilDocument` | Civil 3D document (surfaces, alignments, points, styles ...) |
| `Database` | `Database` | Drawing database |
| `Transaction` | `Transaction` | Already open. Don't commit it yourself |
| `Editor` | `Editor` | Command line / selection |
| `Ctx` | plugin helper | `Ctx.Find("EG")` (name or handle → ObjectId), `Ctx.Add(entity)`, `Ctx.StyleId(category, name)`, `Ctx.EnsureLayer(name)` |
| `Inputs` | `Dictionary<string, object>` | Values sent with the call |
| `Print(x)` | method | Notes returned in `log` |

Auto-imported: `System`, `System.Linq`, `System.Collections.Generic`, `Autodesk.AutoCAD.*` (ApplicationServices,
DatabaseServices, Geometry, EditorInput, Colors), `Autodesk.Civil`, `Autodesk.Civil.ApplicationServices`,
`Autodesk.Civil.DatabaseServices`, `Civil3DMcp`. In scripts, `Entity` means the AutoCAD entity and `Surface` means the Civil 3D surface.

---

## Setup

### Requirements
- Autodesk **Civil 3D 2025 or 2026** (full version, Windows). For 2024 and older, see [Older versions](#older-civil-3d-versions).
- **.NET 8 SDK**: https://dotnet.microsoft.com/download/dotnet/8.0. Only needed to *build*; `release/` has a ready-made plugin.
- **Python 3.10+**: https://python.org (tick *Add python.exe to PATH*)
- No NuGet packages: code execution uses the Roslyn compiler that already ships with AutoCAD.
- **Claude Desktop** (or any MCP client)

### 1. Get the code
```bat
git clone https://github.com/YOUR-NAME/civil3d-mcp.git C:\civil3d-mcp
```

### 2. Check the Civil 3D path
In `civil3d-plugin\Civil3DMcp.csproj`, `AcadDir` must be the folder that contains `acad.exe`:
```xml
<AcadDir>C:\Program Files\Autodesk\AutoCAD 2025</AcadDir>
```

### 3. Install the plugin (close Civil 3D first)
**Quick:** double-click `release\install_prebuilt.bat`. It installs the ready-made plugin, built and tested on Civil 3D 2025.

**From source:** double-click `build_and_install.bat`. It builds the plugin, then installs it.

Either way, the plugin goes into the Civil 3D **autoloader** (so it loads automatically every time Civil 3D starts)
and the Python requirements are installed.

### 4. Configure the AI
**Claude Desktop** → *Settings → Developer → Edit Config*:
```json
{
  "mcpServers": {
    "civil3d": {
      "command": "python",
      "args": ["C:\\civil3d-mcp\\mcp-server\\server.py"],
      "env": { "CIVIL3D_MCP_MODE": "full" }
    }
  }
}
```
**Claude Code:** `claude mcp add civil3d -- python C:\civil3d-mcp\mcp-server\server.py`

Restart Claude Desktop after editing the config.

### 5. Use it
1. Open **Civil 3D** and open a drawing. The command line shows `[Civil3D MCP] Ready. Listening on 127.0.0.1:8765`.
2. Open **Claude** side by side.
3. Ask: *"Ping Civil 3D"*, then start working.

Civil 3D commands: `MCPSTATUS` · `MCPSTOP` · `MCPSTART` · `MCPTEST` (self-test). If the plugin doesn't load, use `NETLOAD` and pick
`%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcp.bundle\Contents\Civil3DMcp.dll`.

---

## Environment variables

| Variable | Default | Description |
|---|---|---|
| `CIVIL3D_MCP_MODE` | `full` | `full` · `lean` · `code` (see [Modes](#modes-token-control)) |
| `CIVIL3D_MCP_HOST` | `127.0.0.1` | Where the plugin listens |
| `CIVIL3D_MCP_PORT` | `8765` | Plugin port (the plugin's port is set in `PluginEntry.cs`) |
| `CIVIL3D_MCP_TIMEOUT` | `300` | Seconds to wait for Civil 3D |
| `CIVIL3D_SKILLS_DIR` | `../skills` | Folder with `.skill.md` templates |

---

## Security

- The plugin listens **only on 127.0.0.1**, so no other computer can connect.
- Every call runs in **one transaction**. If it fails, nothing is half-changed. `code_query` **always rolls back**.
- `code_execute` / `code_query` reject scripts that try to: start programs, delete or move files, use the network
  (HttpClient, WebClient, sockets), use the registry, load assemblies, close Civil 3D, or run unsafe/native code.
  All Civil 3D API operations are allowed.
- This is a **text filter, not a full sandbox**. Only use the server with AI clients and drawings you trust, and keep backups.

---

## Project structure

```
civil3d-mcp/
├── README.md · LICENSE · build_and_install.bat · claude_desktop_config.example.json
├── docs/tool-reference.md
├── skills/                          C# templates for code execution
├── mcp-server/                      Python MCP server
│   ├── server.py                    tool list, modes, routing
│   ├── bridge.py                    TCP client → plugin
│   ├── skills_lib.py                skills_list / search / read
│   ├── tools/                       tool catalogue (one file per group)
│   └── tests/test_server.py         catalogue ↔ plugin check + end-to-end test
├── release/                         ready-made plugin bundle + install_prebuilt.bat
└── civil3d-plugin/                  .NET 8 Civil 3D plugin
    ├── PluginEntry.cs · Listener.cs · Router.cs · SelfTest.cs (MCPTEST)
    ├── Util/                        Args, Ctx, Conv, Reflect, Props, MainThread
    ├── Handlers/                    Core, Drafting, Modify, Layers, Annotation, Blocks, Layouts, Code, Civil/*
    └── Bundle/PackageContents.xml   autoloader
```

### Adding a tool
1. C#: `[Tool("group.my_tool")] public static object MyTool(Args a, Ctx c) { ... }`
2. Python: add `"group.my_tool": ("What it does", {"param": ("string", "description", True)})` in `mcp-server/tools/`.
3. Run `python mcp-server/tests/test_server.py`.

### Older Civil 3D versions
Civil 3D 2025 and 2026 use .NET 8 (this project). Civil 2024 and older use .NET Framework 4.8. Porting to them means
changing `net8.0` to `net48` in the `.csproj`, adding `System.Text.Json`, and replacing `AssemblyLoadContext` in
`CodeHandler.cs`. This hasn't been done or tested yet.

---

## Status: tested in Civil 3D 2025

The plugin was compiled and tested **live in Civil 3D 2025 (Metric)** with the built-in self-test (`MCPTEST`), which sends
real tool calls through the same 127.0.0.1:8765 connection the MCP server uses.

| Area | Result |
|---|---|
| Drafting, modify, layers, linetypes | ✅ all pass |
| Text, MText, tables, text / dimension / mleader styles, all 9 dimension types | ✅ all pass |
| Blocks, layouts, viewports, **PDF plotting** | ✅ all pass |
| Properties (`props_get / set / methods / call`), queries, sysvars | ✅ all pass |
| Code execution (`code_execute` / `code_query`) + 6 skills run live | ✅ pass (safety filter and compile-error reporting verified) |
| COGO points, CSV import, point groups, point styles | ✅ all pass |
| Surfaces: create, point groups, breaklines, boundary, contour style + smoothing, extract contours, analysis, cut/fill volume | ✅ all pass |
| Alignments (PIs + curves, polyline, offset, equations, speeds), profiles (EG, FG + vertical curves), profile views | ✅ all pass |
| Sample lines, section views, assembly, corridor, feature lines, sites | ✅ all pass |
| Pipe networks: create network, parts lists, info | ✅ pass |
| Pipe networks: add pipes / structures | ⚠️ needs Civil 3D's pipe catalog set (*Manage tab → Set Pipe Network Catalog*); the tool reports this clearly |

Full suite: **158 of 160 checks passed**; the other 2 were deliberate error tests that behaved correctly.

Run the self-test yourself: copy `civil3d-plugin\SelfTest\mcp-tests.example.json` next to the installed DLL as
`mcp-tests.json`, type `MCPTEST` in Civil 3D, and read `mcp-results.json` from the same folder.

**Not built in yet** (use `code_execute` or `doc_run_command`): survey database, creating pressure networks, adding
subassemblies to assemblies, superelevation, QTO reports, LandXML import/export, creating parcels from objects
(Civil 3D only offers that in its UI: `_CreateParcelFromObjects`).

---

## License

MIT. See [LICENSE](LICENSE).

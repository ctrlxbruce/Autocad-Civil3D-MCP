# Civil 3D MCP: control Autodesk Civil 3D with AI

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
![Civil 3D 2025 | 2026](https://img.shields.io/badge/Civil%203D-2025%20%7C%202026-0696D7)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6)
![Python 3.10+](https://img.shields.io/badge/Python-3.10%2B-3776AB)
![MCP](https://img.shields.io/badge/MCP-server-black)

**Tell Claude what you need in plain English, and it does the work inside your open Civil 3D drawing.**

> *"Import survey.csv, build an EG surface with 1 m contours, then give me the cut/fill volume against FG."*

Civil 3D MCP connects AI assistants (**Claude Desktop**, **Claude Code**, Cline and other MCP clients) to Autodesk Civil 3D.
It ships with **157 ready-made tools** for drafting, COGO points, surfaces, alignments, profiles, corridors, pipe networks and
PDF plotting. For anything those tools don't cover, Claude can write and run C# code inside Civil 3D, starting from a
library of tested templates.

---

## Contents

- [What you can do](#what-you-can-do)
- [Requirements](#requirements)
- [Installation](#installation-about-10-minutes)
- [First use](#first-use)
- [Example prompts](#example-prompts)
- [Modes: control token use](#modes-control-token-use)
- [Troubleshooting](#troubleshooting)
- [Safety and privacy](#safety-and-privacy)
- [Updating and uninstalling](#updating-and-uninstalling)
- [What is included](#what-is-included)
- [Known limitations](#known-limitations)
- [For developers](#for-developers)
- [License](#license)

---

## What you can do

| Area | Examples |
|---|---|
| **Drafting** | Lines, polylines, 3D polylines, rectangles, circles, arcs, hatches; move, copy, rotate, offset, mirror, arrays |
| **Layers and annotation** | Create layer standards, text, MText, tables, leaders, all 9 dimension types, text and dimension styles |
| **Blocks and sheets** | Insert blocks with attributes, xrefs, layouts, viewports, **plot to PDF** |
| **COGO points** | Create, edit, **import CSV/TXT**, point groups, point styles, description keys |
| **Surfaces** | TIN/grid surfaces, breaklines, boundaries, **contour styles and smoothing**, analysis, **cut/fill volumes** |
| **Alignments and profiles** | Alignments from polylines or PIs (curves and spirals), offsets, EG profiles, FG profiles, profile views |
| **Corridors and sections** | Assemblies, corridors, corridor surfaces, sample lines, section views, feature lines, sites |
| **Pipe networks** | Networks, parts lists, structures, pipes, rules, network reports |
| **Anything else** | Claude writes C# that runs inside Civil 3D with the full .NET API |

---

## Requirements

| You need | Notes |
|---|---|
| **Windows 10 or 11** (64-bit) | |
| **Autodesk Civil 3D 2025 or 2026** | Full Civil 3D. Plain AutoCAD is not supported. Tested on Civil 3D 2025; 2026 is supported but not yet tested. |
| **Python 3.10 or newer** | Download from [python.org](https://www.python.org/downloads/). During setup, tick **"Add python.exe to PATH"**. |
| **An MCP client** | [Claude Desktop](https://claude.ai/download) (recommended), Claude Code, Cline, or another MCP client |

You do **not** need Visual Studio or the .NET SDK. A ready-made plugin is included.

---

## Installation (about 10 minutes)

### Step 1: Download the project

**Option A: with Git**

```bat
git clone https://github.com/ctrlxbruce/Autocad-Civil3D-MCP.git C:\civil3d-mcp
```

**Option B: download as ZIP**

1. On this page, click the green **Code** button, then **Download ZIP**.
2. Right-click the ZIP, choose **Properties**, tick **Unblock** if you see it, and click **OK**.
3. Extract it, rename the folder from `Autocad-Civil3D-MCP-main` to `civil3d-mcp`, and move it to `C:\`.

You should now have `C:\civil3d-mcp\README.md`.

> [!TIP]
> You can keep the folder somewhere else. Just use your own path in Step 3 instead of `C:\civil3d-mcp`.

### Step 2: Install the plugin

1. **Close Civil 3D.**
2. Open `C:\civil3d-mcp\release\` and double-click **`install_prebuilt.bat`**.
   If Windows shows *"Windows protected your PC"*, click **More info**, then **Run anyway**.
3. Wait for **DONE**, then press any key.

The installer does two things:
- copies the plugin to `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcp.bundle`, so Civil 3D loads it automatically every time it starts;
- installs the two Python packages the server needs (`mcp` and `anyio`).

### Step 3: Connect Claude

**Claude Desktop**

1. Open Claude Desktop and go to **Settings → Developer → Edit Config**. This opens `claude_desktop_config.json`.
2. Paste the following and save:

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

   If the file already has other servers, add only the `"civil3d": { ... }` part inside the existing `"mcpServers"`.
   Keep the double backslashes (`\\`) in the path.

3. **Fully quit Claude Desktop** (right-click its icon in the system tray, then **Quit**) and open it again.

**Claude Code**

```bat
claude mcp add civil3d -e CIVIL3D_MCP_MODE=full -- python C:\civil3d-mcp\mcp-server\server.py
```

**Other MCP clients** (Cline, Cursor and others): add a server with command `python` and argument
`C:\civil3d-mcp\mcp-server\server.py`.

---

## First use

1. Start **Civil 3D** and open a drawing.
   The command line should show:
   ```
   [Civil3D MCP] Ready. Listening on 127.0.0.1:8765. ... tools loaded.
   ```
2. Open **Claude** next to it.
3. Type **"Ping Civil 3D"**. If Claude answers that Civil 3D is connected, you're ready.

> [!NOTE]
> Civil 3D must be open with a drawing loaded whenever you ask Claude to work in it.

---

## Example prompts

Start simple, then build up. Claude picks the right tools by itself.

| Task | Try asking |
|---|---|
| Check the drawing | *"What surfaces, alignments and point groups are in this drawing?"* |
| Count objects | *"How many objects are on each layer?"* |
| Layer standard | *"Create layers C-ROAD (red), C-WATR (blue) and C-TOPO (green)."* |
| Simple drafting | *"Draw a 20 × 10 rectangle at 0,0 on layer C-BLDG and hatch it."* |
| Import survey | *"Import C:\survey\points.csv (PNEZD, comma delimited) into a point group called SURVEY."* |
| Build a surface | *"Create a surface EG from the point group SURVEY with 1 m major and 0.25 m minor smooth contours."* |
| Volumes | *"Calculate cut and fill between EG and FG."* |
| Road design | *"Make an alignment from the centerline polyline, then create its EG profile and a profile view."* |
| Sections | *"Create sample lines every 20 m along the alignment and draw the section views."* |
| Pipes | *"List every pipe in the Storm network with its length, size and slope."* |
| Sheets | *"Plot the current layout to PDF."* |

Coordinates and distances use your drawing's units.

---

## Modes: control token use

Claude receives the full tool list at the start of every chat. Fewer tools means fewer tokens per chat.
Set the mode with `CIVIL3D_MCP_MODE` in the config from Step 3.

| Mode | Tools | Tool-list size | Best for |
|---|---|---|---|
| `full` (default) | 157 | ~22,600 tokens | Everyday work. Claude picks a ready tool, so calls are short and rarely fail. |
| `lean` | 32 | ~4,800 tokens | Common drafting tools, property tools, code execution and skills |
| `code` | 8 | ~1,000 tokens | Claude writes C# for everything, using the skills library |

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Claude says **"Civil 3D is not reachable"** | Open Civil 3D **and a drawing**. Type `MCPSTATUS` in Civil 3D. If it says STOPPED, type `MCPSTART`. |
| `MCPSTATUS` is an unknown command | The plugin isn't loaded. Close Civil 3D and run `install_prebuilt.bat` again. To load it once by hand, type `NETLOAD` and pick `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcp.bundle\Contents\Civil3DMcp.dll`. |
| The civil3d tools don't appear in Claude | Check the config file: the path must be correct, use `\\`, and have no missing commas. Then fully quit and reopen Claude Desktop. |
| `'python' is not recognized` | Reinstall Python and tick **"Add python.exe to PATH"**, then run `install_prebuilt.bat` again. Or put the full path to `python.exe` in `"command"`. |
| **"Civil 3D did not answer in time"** | A dialog box is probably open in Civil 3D. Close it and try again. |
| Can't add pipes or structures | Set the pipe catalog first: **Manage tab → Set Pipe Network Catalog**. |
| Something else | Open an [issue](https://github.com/ctrlxbruce/Autocad-Civil3D-MCP/issues) with the error message and your Civil 3D version. |

**Civil 3D commands added by the plugin**

| Command | What it does |
|---|---|
| `MCPSTATUS` | Shows whether the plugin is running |
| `MCPSTOP` / `MCPSTART` | Stops or starts the connection |
| `MCPTEST` | Runs the built-in self-test (see [For developers](#for-developers)) |

---

## Safety and privacy

- **Local only.** The plugin accepts connections only from your own computer (`127.0.0.1`). No other computer can connect to it.
- **Your AI service sees what Claude reads.** Drawing information that Claude looks at (names, coordinates, properties)
  becomes part of your chat with the AI service, just like anything else you type.
- **No half-finished changes.** Every request runs in one transaction. If it fails, the drawing is left as it was.
  Read-only code (`code_query`) always rolls back.
- **Code filter.** Before running C# code, the plugin rejects scripts that try to start programs, delete or move files,
  use the network or registry, load other assemblies, close Civil 3D, or run unsafe code.

> [!WARNING]
> The code filter is a text check, not a full sandbox. Use this only with drawings and AI clients you trust,
> **save your work often**, and keep backups of important drawings.

---

## Updating and uninstalling

**Update**

1. Get the new version: run `git pull` in `C:\civil3d-mcp`, or download the ZIP again and replace the folder.
2. Close Civil 3D and run `release\install_prebuilt.bat` again.

**Uninstall**

1. Close Civil 3D and delete the folder `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcp.bundle`.
2. Remove the `"civil3d"` entry from your Claude config.
3. Delete `C:\civil3d-mcp`.

---

## What is included

| Group | Tools | Examples |
|---|---|---|
| **Code and skills** | 5 | `code_execute` (changes the drawing), `code_query` (read-only), `skills_list`, `skills_search`, `skills_read` |
| **Core and properties** | 17 | `doc_info`, `query_list`, `props_get` / `props_set` (any property of any object), `props_call` (any API method), `doc_run_command` |
| **Drafting** | 14 | Line, polyline, 3D polyline, rectangle, polygon, circle, arc, ellipse, spline, point, hatch, xline, ray, donut |
| **Modify** | 12 | Move, copy, rotate, scale, mirror, offset, explode, join, rectangular and polar array, match properties, vertex edit |
| **Layers** | 6 | Create and edit layers, set the current layer, delete, load linetypes |
| **Annotation and styles** | 20 | Text, MText, table, multileader; text, dimension and multileader styles; linear, aligned, angular, radius, diameter, arc length, ordinate and polyline-segment dimensions |
| **Blocks** | 7 | Define, insert with attributes, dynamic properties, import from DWG, xref |
| **Layouts** | 7 | Layouts, viewports, paper sizes, plot to PDF |
| **COGO points** | 11 | Create, list, edit, delete, import CSV/TXT, elevations from surface, point groups, point styles, description keys |
| **Surfaces** | 15 | TIN/grid, point groups, points, breaklines, contours, boundaries, DEM, `.tin` import, edit, contour style and smoothing, extract contours, analysis, cut/fill volume |
| **Alignments and profiles** | 15 | From polyline or PIs (curves and spirals), offset, station/offset, equations, design speeds, EG profile, FG by PVIs, profile views |
| **Corridors and sections** | 11 | Assembly, corridor, corridor surface, sample lines, section views, feature lines, sites, parcels |
| **Pipes** | 9 | Parts lists, network, structures, pipes, connect, rules, report, pressure network info |
| **Civil styles** | 8 | List Civil objects; list, create, get and set any style collection; display components |

Every tool with its inputs: [`docs/tool-reference.md`](docs/tool-reference.md)

**Skills library** (tested C# templates Claude can adapt):

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

---

## Known limitations

- **Civil 3D 2024 and older** are not supported yet (they use .NET Framework 4.8; see [For developers](#for-developers)).
- **Adding pipes and structures** needs Civil 3D's pipe catalog to be set (*Manage tab → Set Pipe Network Catalog*).
- **Not built in as tools yet:** survey database, creating pressure networks, adding subassemblies to assemblies,
  superelevation, QTO reports, LandXML import/export, and creating parcels from objects.
  Claude can still try these through code execution or Civil 3D commands.

**Test status:** the plugin was tested live in Civil 3D 2025 (Metric) with the built-in self-test.
158 of 160 checks passed; the other 2 were deliberate error tests that behaved correctly.

---

## For developers

<details>
<summary><b>Architecture</b></summary>

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

</details>

<details>
<summary><b>Build the plugin from source</b></summary>

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. In `civil3d-plugin\Civil3DMcp.csproj`, set `AcadDir` to the folder that contains `acad.exe`:
   ```xml
   <AcadDir>C:\Program Files\Autodesk\AutoCAD 2025</AcadDir>
   ```
3. Close Civil 3D and double-click `build_and_install.bat`. It builds the plugin, installs it into the autoloader and
   installs the Python requirements.

No NuGet packages are needed: code execution uses the Roslyn compiler that ships with AutoCAD.

</details>

<details>
<summary><b>How code execution works</b></summary>

1. Claude searches the skills library (`skills_search "contour smoothing"`).
2. Claude reads the template (`skills_read`) and adapts it to your drawing.
3. Claude sends it with `code_query` (read-only) or `code_execute` (changes the drawing).
4. The Roslyn engine compiles and runs it inside Civil 3D, on the main thread, inside an open transaction.
5. The script's `return` value comes back as JSON.

Example (`code_query`):

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

Result:

```json
{ "result": [ { "Name": "EG", "NumberOfPoints": 18452, "MinimumElevation": 212.4, "MaximumElevation": 248.9 } ], "log": [] }
```

**Script globals**

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

**Add your own skill:** drop a new `*.skill.md` file into a folder under `skills/`. Each file has a short header
(name, category, description, tags, mode), example inputs and the C# code. No rebuild is needed.

</details>

<details>
<summary><b>Environment variables</b></summary>

| Variable | Default | Description |
|---|---|---|
| `CIVIL3D_MCP_MODE` | `full` | `full` · `lean` · `code` (see [Modes](#modes-control-token-use)) |
| `CIVIL3D_MCP_HOST` | `127.0.0.1` | Where the plugin listens |
| `CIVIL3D_MCP_PORT` | `8765` | Plugin port. The plugin's own port is set in `PluginEntry.cs`, so changing it means rebuilding. |
| `CIVIL3D_MCP_TIMEOUT` | `300` | Seconds to wait for Civil 3D |
| `CIVIL3D_SKILLS_DIR` | `../skills` | Folder with `.skill.md` templates |

</details>

<details>
<summary><b>Project structure</b></summary>

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

</details>

<details>
<summary><b>Add a tool</b></summary>

1. C#: `[Tool("group.my_tool")] public static object MyTool(Args a, Ctx c) { ... }`
2. Python: add `"group.my_tool": ("What it does", {"param": ("string", "description", True)})` in `mcp-server/tools/`.
3. Run `python mcp-server/tests/test_server.py`.

</details>

<details>
<summary><b>Run the self-test</b></summary>

Copy `civil3d-plugin\SelfTest\mcp-tests.example.json` next to the installed DLL as `mcp-tests.json`, type `MCPTEST`
in Civil 3D, and read `mcp-results.json` from the same folder. The self-test sends real tool calls through the same
127.0.0.1:8765 connection the MCP server uses.

</details>

<details>
<summary><b>Older Civil 3D versions</b></summary>

Civil 3D 2025 and 2026 use .NET 8 (this project). Civil 3D 2024 and older use .NET Framework 4.8. Porting means
changing `net8.0` to `net48` in the `.csproj`, adding `System.Text.Json`, and replacing `AssemblyLoadContext` in
`CodeHandler.cs`. This hasn't been done or tested yet. Contributions are welcome.

</details>

---

## License

MIT. See [LICENSE](LICENSE).

This is an independent community project. It is not affiliated with, endorsed by or supported by Autodesk, Inc. or
Anthropic. Autodesk, AutoCAD and Civil 3D are trademarks of Autodesk, Inc. Claude is a trademark of Anthropic.

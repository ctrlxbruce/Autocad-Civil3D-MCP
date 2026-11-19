# Civil 3D MCP: control Autodesk Civil 3D with AI

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Version 1.1.0](https://img.shields.io/badge/version-1.1.0-blue)](CHANGELOG.md)
![Civil 3D 2025 | 2026](https://img.shields.io/badge/Civil%203D-2025%20%7C%202026-0696D7)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6)
![Python 3.10+](https://img.shields.io/badge/Python-3.10%2B-3776AB)
![MCP](https://img.shields.io/badge/MCP-server-black)

**Tell Claude what you need in plain English, and it does the work inside your open Civil 3D drawing.**

> *"Import survey.csv, build an EG surface with 1 m contours, then give me the cut/fill volume against FG."*

Civil 3D MCP connects AI assistants (**Claude Desktop**, **Claude Code**, Cline and other MCP clients) to Autodesk Civil 3D.
It ships with **181 ready-made tools** for drafting, COGO points, surfaces, alignments, profiles, corridors, sewer and
water networks, hydrology, quantities and PDF plotting. For anything those tools don't cover, Claude can write and run C#
code inside Civil 3D, starting from a library of tested templates.

**Install in one message.** In Claude Code (or the **Code** tab of the Claude desktop app), send
`Install this repo: https://github.com/ctrlxbruce/Autocad-Civil3D-MCP`. Then open Civil 3D and tell Claude
*"Use Civil 3D"*. [Other ways to install](#installation).

---

## Contents

- [What you can do](#what-you-can-do)
- [Requirements](#requirements)
- [Installation](#installation)
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
- [Changelog](CHANGELOG.md)

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
| **Corridors and sections** | Assemblies with **stock subassemblies** (lanes, curbs, shoulders, daylight), corridors, corridor surfaces, sample lines, section views, feature lines, sites |
| **Sewer and storm networks** | Networks, structures, pipes with inverts and slopes, rules, **Manning capacity / velocity / d/D, simplified HGL**, network QC (slope, cover, sumps) |
| **Water supply (pressure) networks** | Parts lists, networks, pipes placed at a cover depth, fittings, valves and hydrants, **cover check** (see [Known limitations](#known-limitations)) |
| **Hydrology** | **Rational method** runoff, **time of concentration** (Kirpich, California / IRC:SP:13, FAA), catchment area, low point, flow path |
| **Quantities and cost** | Pipe lengths by size, structure and fitting counts, earthwork from volume surfaces, **BOQ with rates and CSV export** |
| **Road checks** | Stopping sight distance, vertical curve **K values** and horizontal curve radii against a design speed (AASHTO or IRC) |
| **Large drawings** | A short **drawing summary**, and long results are trimmed automatically so the chat stays readable |
| **Anything else** | Claude writes C# that runs inside Civil 3D with the full .NET API |

---

## Requirements

| You need | Notes |
|---|---|
| **Windows 10 or 11** (64-bit) | |
| **Autodesk Civil 3D 2025 or 2026** | Full Civil 3D. Plain AutoCAD is not supported. Tested on Civil 3D 2025; 2026 is supported but not yet tested. Civil 3D 2027 runs on .NET 10 and is not supported yet. |
| **Python 3.10 or newer** | The installer finds it, even if it isn't on PATH. Don't have it? Get it from [python.org](https://www.python.org/downloads/) (tick **"Add python.exe to PATH"**) or run `winget install -e --id Python.Python.3.12`. |
| **Claude** | [Claude Desktop](https://claude.ai/download) and/or Claude Code. Other MCP clients (Cline, Cursor ...) work with the [manual setup](#installation). |

You do **not** need Visual Studio or the .NET SDK. A ready-made plugin is included.

---

## Installation

Pick one way. Each one ends in the same setup.

### Option A: ask Claude to install it (easiest)

This needs **Claude Code**: the **Code** tab in the Claude desktop app, or the `claude` command in a terminal.
The normal Claude chat can't run programs on your computer, so it can't install anything.

1. Close Civil 3D.
2. Send Claude this message:

   ```
   Install this repo: https://github.com/ctrlxbruce/Autocad-Civil3D-MCP
   ```

3. Claude downloads the project to `C:\civil3d-mcp` and runs the installer. Approve the steps when Claude asks.
4. When Claude says it's done, fully quit Claude Desktop (right-click its icon in the system tray, then **Quit**)
   and open it again. In Claude Code, start a new session.

### Option B: one-click installer

1. Download the project:
   - **With Git:** `git clone https://github.com/ctrlxbruce/Autocad-Civil3D-MCP.git C:\civil3d-mcp`
   - **As a ZIP:** click the green **Code** button on this page, then **Download ZIP**. Right-click the ZIP, choose
     **Properties**, tick **Unblock** if you see it, click **OK**, then extract it. Any folder works.
2. Close Civil 3D.
3. Double-click **`install.bat`** in the project folder.
   If Windows shows *"Windows protected your PC"*, click **More info**, then **Run anyway**.
4. When it says **Civil 3D MCP is installed**, fully quit Claude Desktop and open it again.

### What the installer does

| Step | What happens |
|---|---|
| 1 | Checks that Civil 3D is closed, and looks for Civil 3D 2025 / 2026 |
| 2 | Copies the plugin to `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcp.bundle`, so Civil 3D loads it every time it starts |
| 3 | Finds Python 3.10+ (even if it isn't on PATH) and creates a private environment in `mcp-server\.venv` with the packages the server needs. If the project folder is too deep for Windows' 260-character path limit, it uses `%LOCALAPPDATA%\Civil3DMcp\venv` instead |
| 4 | Adds the `civil3d` server to **Claude Desktop** and **Claude Code**, whichever you have. Your other Claude settings are kept, and the old Claude Desktop config is saved as `claude_desktop_config.json.civil3d-backup` |
| 5 | Installs the **civil3d skill** for Claude Code, so you can type `/civil3d` |

You can run it again at any time; it updates the existing setup. To load fewer tools, run
`install.bat -Mode lean` (see [Modes](#modes-control-token-use)).

<details>
<summary><b>Option C: manual setup (other MCP clients, or if you prefer)</b></summary>

1. Close Civil 3D and copy the folder `release\Civil3DMcp.bundle` into `%APPDATA%\Autodesk\ApplicationPlugins\`.
2. Install the Python packages: `py -3 -m pip install -r C:\civil3d-mcp\mcp-server\requirements.txt`
3. **Claude Desktop:** open **Settings → Developer → Edit Config** and add:

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

   If the file already has other servers, add only the `"civil3d": { ... }` part inside `"mcpServers"`, and keep
   the double backslashes. If `python` doesn't start on your PC, put the full path to `python.exe` in `"command"`.
   Then fully quit Claude Desktop and open it again.
4. **Claude Code:** `claude mcp add civil3d --scope user --env CIVIL3D_MCP_MODE=full -- python C:\civil3d-mcp\mcp-server\server.py`
5. **Other MCP clients** (Cline, Cursor and others): add a server with command `python` and argument
   `C:\civil3d-mcp\mcp-server\server.py`.

</details>

---

## First use

1. Start **Civil 3D** and open a drawing. The command line shows:
   ```
   [Civil3D MCP] Ready. Listening on 127.0.0.1:8765. ... tools loaded.
   ```
2. Open **Claude** and put the two windows side by side: press **Windows + ←** on one and **Windows + →** on the other.
3. In Claude, type:

   ```
   Use Civil 3D
   ```

   Claude checks the connection and sums up what is in your drawing. In Claude Code you can also type **`/civil3d`**.
4. Now ask for what you need, for example *"Draw a 20 × 10 rectangle on layer C-BLDG"*. The change appears in
   Civil 3D right away.

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
| Sewer design check | *"Check the Sanitary network with these peak flows in L/s: S-1 5, S-2 8. Flag velocities under 0.6 m/s."* |
| Storm runoff | *"Peak runoff for the catchment inside this polyline, C 0.6, 50 mm/h. Then the time of concentration from a flow path."* |
| Road check | *"Check the FG profile's vertical curves for 60 km/h with IRC values."* |
| Bill of quantities | *"Make a BOQ: 200 mm PVC pipe at ₹850/m, manholes at ₹25,000 each, export to CSV."* |
| Big drawing | *"Summarise this drawing first, then list the layers with the most objects."* |
| Sheets | *"Plot the current layout to PDF."* |

Coordinates and distances use your drawing's units.

---

## Modes: control token use

Claude receives the full tool list at the start of every chat. Fewer tools means fewer tokens per chat.
Set the mode with `CIVIL3D_MCP_MODE` in the config from Step 3.

| Mode | Tools | Tool-list size | Best for |
|---|---|---|---|
| `full` (default) | 181 | ~25,600 tokens | Everyday work. Claude picks a ready tool, so calls are short and rarely fail. |
| `lean` | 33 | ~4,900 tokens | Common drafting tools, property tools, drawing summary, code execution and skills |
| `code` | 8 | ~1,000 tokens | Claude writes C# for everything, using the skills library |

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Installer: **"Civil 3D (or AutoCAD) is running"** | Close Civil 3D (check the taskbar), then run the installer again. |
| Installer: **"Python 3.10 or newer was not found"** | Install Python from [python.org](https://www.python.org/downloads/) or run `winget install -e --id Python.Python.3.12`, then run the installer again. |
| Claude says **"Civil 3D is not reachable"** | Open Civil 3D **and a drawing**. Type `MCPSTATUS` in Civil 3D. If it says STOPPED, type `MCPSTART`. |
| `MCPSTATUS` is an unknown command | The plugin isn't loaded. Close Civil 3D and run `install.bat` again. To load it once by hand, type `NETLOAD` and pick `%APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcp.bundle\Contents\Civil3DMcp.dll`. |
| The civil3d tools don't appear in Claude | Run `install.bat` again and read its summary. Then fully quit and reopen Claude Desktop, or start a new Claude Code session (`claude mcp list` should show `civil3d`). |
| **"Civil 3D did not answer in time"** | A dialog box is probably open in Civil 3D. Close it and try again. |
| **"Missing or wrong token"** | Happens right after updating from 1.0. Close Civil 3D, run `install.bat` again, then fully quit and reopen Claude Desktop (or start a new Claude Code session) and open Civil 3D. |
| **"Blocked for safety"** | The command or code does something the plugin doesn't allow (see [Safety and privacy](#safety-and-privacy)). Do that step yourself in Civil 3D. |
| Can't add pipes or structures | Set the pipe catalog for the drawing: **Home tab → Create Design → Pipe Network ▾ → Set Pipe Network Catalog** (or type `AECCSETNETWORKCATALOG`; for pressure networks `AECCSETPRESSURENETWORKCATALOG`). Folder for Civil 3D 2025: `C:\ProgramData\Autodesk\C3D 2025\enu\Pipes Catalog`; choose **Metric Pipes** and **Metric Structures** for metric drawings. If the parts list still has no sizes, start the drawing from `_Autodesk Civil 3D (Metric) NCS.dwt`. |
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
- **Secret token.** Each time Civil 3D starts, the plugin makes a new random token and saves it in
  `%LOCALAPPDATA%\Civil3DMcp\`, which only your Windows user can read. A request without the right token is
  refused, and anything that isn't a plain MCP request (for example a web page trying to reach the plugin through
  your browser) closes the connection at once.
- **Your AI service sees what Claude reads.** Drawing information that Claude looks at (names, coordinates, properties)
  becomes part of your chat with the AI service, just like anything else you type.
- **No half-finished changes.** Every request runs in one transaction. If it fails, the drawing is left as it was.
  Read-only code (`code_query`) always rolls back.
- **Blocked commands.** `doc_run_command` refuses commands that start programs or open files outside Civil 3D
  (`SHELL`, `START`, `BROWSER`…), load code (`NETLOAD`, `APPLOAD`, `SCRIPT`, VBA), close Civil 3D, and AutoLISP.
  `doc_set_sysvar` can't change security settings such as `SECURELOAD` or `TRUSTEDPATHS`.
- **Code check.** Before running C# code, the plugin checks every name the compiler resolves (not just the text) and
  rejects scripts that start programs, write, move or delete files, use the network, registry or native code,
  call methods through reflection or `dynamic`, send text to the command line, or close Civil 3D. Reading files and
  all drawing work are allowed.
- **Tool labels.** Every tool tells Claude whether it only reads, only adds new objects, or can change or delete
  existing ones, so Claude Desktop and Claude Code can ask you before the risky ones.

> [!WARNING]
> The code check is a safety net, not a full sandbox. Use this only with drawings and AI clients you trust,
> **save your work often**, and keep backups of important drawings.

---

## Updating and uninstalling

**Update**

- Ask Claude Code: *"Update the Civil 3D MCP repo in C:\civil3d-mcp and run its installer again"*, or
- run `git pull` in the project folder (or download the ZIP again), close Civil 3D, and double-click `install.bat`.

**Uninstall**

1. Close Civil 3D and Claude.
2. Double-click **`uninstall.bat`** in the project folder. It removes the plugin, the `civil3d` entry from Claude
   Desktop and Claude Code, the `/civil3d` skill and the Python environment. Your other Claude settings are kept.
3. Delete the project folder.

---

## What is included

| Group | Tools | Examples |
|---|---|---|
| **Code and skills** | 5 | `code_execute` (changes the drawing), `code_query` (read-only), `skills_list`, `skills_search`, `skills_read` |
| **Core and properties** | 18 | `doc_info`, `doc_summary`, `query_list`, `props_get` / `props_set` (any property of any object), `props_call` (any API method), `doc_run_command` |
| **Drafting** | 14 | Line, polyline, 3D polyline, rectangle, polygon, circle, arc, ellipse, spline, point, hatch, xline, ray, donut |
| **Modify** | 12 | Move, copy, rotate, scale, mirror, offset, explode, join, rectangular and polar array, match properties, vertex edit |
| **Layers** | 6 | Create and edit layers, set the current layer, delete, load linetypes |
| **Annotation and styles** | 20 | Text, MText, table, multileader; text, dimension and multileader styles; linear, aligned, angular, radius, diameter, arc length, ordinate and polyline-segment dimensions |
| **Blocks** | 7 | Define, insert with attributes, dynamic properties, import from DWG, xref |
| **Layouts** | 7 | Layouts, viewports, paper sizes, plot to PDF |
| **COGO points** | 11 | Create, list, edit, delete, import CSV/TXT, elevations from surface, point groups, point styles, description keys |
| **Surfaces** | 15 | TIN/grid, point groups, points, breaklines, contours, boundaries, DEM, `.tin` import, edit, contour style and smoothing, extract contours, analysis, cut/fill volume |
| **Alignments and profiles** | 15 | From polyline or PIs (curves and spirals), offset, station/offset, equations, design speeds, EG profile, FG by PVIs, profile views |
| **Corridors and sections** | 15 | Assembly, stock subassemblies (list, add with hooks, parameters, info), corridor, corridor surface, sample lines, section views, feature lines, sites, parcels |
| **Pipes** | 11 | Parts lists, network, structures, pipes, connect, rules, report, pressure network info, Manning hydraulics with HGL, network QC |
| **Pressure networks** | 6 | Parts lists, network, pipes at a cover depth, fittings, appurtenances (valves, hydrants), cover check |
| **Hydrology** | 5 | Rational method, time of concentration, catchment area, low point, flow path |
| **Quantities and cost** | 3 | Pipe lengths and part counts, earthwork from volume surfaces, BOQ with CSV export |
| **Road checks** | 3 | Stopping sight distance, vertical curve K values, horizontal curve radii (AASHTO or IRC) |
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
- **Adding pipes and structures** needs the pipe catalog to be set
  (*Home tab → Create Design → Pipe Network ▾ → Set Pipe Network Catalog*; see [Troubleshooting](#troubleshooting))
  and a parts list that has sizes. Drawings started from `_Autodesk Civil 3D (Metric) NCS.dwt` come with ready
  "Storm Sewer" and "Sanitary Sewer" parts lists. Adding sizes to an empty parts list through the Civil 3D 2025 API
  does not work, so start pipe work from that template (or one of your own with filled parts lists).
- **Pressure networks** need a pressure parts list with parts. The Metric NCS template's "Water" and "Standard"
  lists are empty, and the API can't add catalog parts to a list, so build one in Civil 3D first (Toolspace →
  Settings → Pressure Network → Parts Lists). On our test PC (Civil 3D 2025.0.1, stock metric catalogs) Civil 3D
  then refused to add pressure pipes, fittings and valves, through the API **and** in its own Pressure Network
  Creation Tools, so adding pressure parts is untested. The tool explains this when it happens. `pressure_check_cover`
  and the quantity tools work on pressure networks drawn in Civil 3D. Updating Civil 3D may fix it.
- **Hydraulics and road checks** use common textbook formulas (Manning, Rational, Kirpich, AASHTO / IRC sight
  distance). Default criteria follow common practice (for example CPHEEO self-cleansing velocity 0.6 m/s); set the
  values from your own design standard and check the results.
- **Not built in as tools yet:** survey database, superelevation, Civil 3D's QTO Manager reports, LandXML
  import/export, and creating parcels from objects. Claude can still try these through code execution or Civil 3D commands.

**Test status (Civil 3D 2025, Metric):** the plugin was built from source and tested live in a blank drawing with
143 tool calls covering every group: drafting, modify, annotation, all 9 dimension types, blocks, layouts and PDF
plotting, COGO points and CSV import, surfaces (breaklines, boundaries, contours, elevation and slope analysis,
cut/fill volume), alignments, profiles, corridors, sample lines and section views, styles, and code execution.
All 143 succeeded, and the code safety filter blocked a file-delete script as intended. Gravity pipe networks were
tested in a drawing from the Metric NCS template: a network with a reference surface, manholes with rims from the
surface, PVC pipes with inverts and falling slopes, connections, design rules and the network report. The one-step
installer was tested on a real PC with Claude Desktop and Claude Code.

Version 1.1.0 added 24 tools, tested live the same way (47 checks, all passed): sewer hydraulics and QC (a 200 mm
PVC pipe at 1 % gives 32.8 L/s full, as by hand), Rational runoff, time of concentration, catchment area, low point
and flow path, stock subassemblies hooked into a corridor, earthwork, pipe lengths and a BOQ with CSV, stopping
sight distance, K values and curve radii (AASHTO and IRC), a decimal-comma point file, and the drawing summary.
Not yet tested: adding pressure parts (see above), xrefs and DWG import, and Civil 3D 2026.

---

## For developers

<details>
<summary><b>Architecture</b></summary>

```
┌──────────────────────┐   stdio   ┌───────────────────────────┐  TCP / JSON lines  ┌──────────────────────────────┐
│  AI Assistant        │ ────────▶ │  MCP Server (Python)      │ ─────────────────▶ │  Civil 3D Plugin (.NET 8)    │
│  Claude Desktop,     │ ◀──────── │  181 tools · 3 modes      │ ◀───────────────── │  176 handlers + Roslyn engine │
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
3. Close Civil 3D and double-click `build_and_install.bat`. It builds the plugin, then runs the normal installer
   with your new build.

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
| `CIVIL3D_MCP_TOKEN_FILE` | `%LOCALAPPDATA%\Civil3DMcp\token-8765` | Where the MCP server reads the plugin's token |
| `CIVIL3D_MCP_ALLOW_LISP` | *(unset)* | Set to `1` **for Civil 3D itself** (not the MCP server) to let `doc_run_command` send AutoLISP |

</details>

<details>
<summary><b>Project structure</b></summary>

```
civil3d-mcp/
├── install.bat · uninstall.bat      one-step installer / uninstaller (Windows)
├── README.md · CHANGELOG.md · CLAUDE.md · LICENSE · build_and_install.bat · claude_desktop_config.example.json
├── .github/ISSUE_TEMPLATE/          bug report and feature request forms
├── scripts/                         install.ps1 · uninstall.ps1 · civil3d-common.ps1
├── claude-skill/civil3d/SKILL.md    the /civil3d skill for Claude Code
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

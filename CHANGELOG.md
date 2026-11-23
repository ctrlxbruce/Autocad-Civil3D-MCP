# Changelog

## Unreleased

**After updating:** close Civil 3D, run `install.bat` again, then restart Claude. The drawing lock needs the new
plugin and the new MCP server together; with only one of them updated everything still works, without the lock.

### New

- **`search` mode** (`install.bat -Mode search`): Claude starts with 10 tools (about 1,100 tokens instead of
  25,900) and still reaches all 183. It finds a tool by words with `tools_search` (for example "pipe slope" or
  "water main") and runs it by name with `tools_read` (read-only tools) or `tools_call` (tools that add, change or
  delete), so Claude can still ask before changing the drawing. Wrong names, missing or misspelt parameters are
  caught before anything reaches Civil 3D. `full` stays the default.
- **Drawing lock**: Claude only changes the drawing it last looked at (`doc_info` or `doc_summary`). If you
  switch to another drawing in the middle of a task, every tool that could change something is refused before it
  runs, nothing is changed, and Claude asks which drawing to work on. Read-only tools still answer, with a note
  that the drawing changed. Set `CIVIL3D_MCP_DRAWING_LOCK=0` to turn it off.
- **Preview (dry run)**: `doc_preview` runs any tool that changes the drawing, including a C# script, and
  reports what it would add, change and delete (type, handle, layer, name), then rolls everything back. Claude is
  told to preview big deletes and edits and ask before doing them. Tools that act outside the drawing (save, plot
  to PDF, command line, layout tabs, xrefs, system variables, CSV export) can't be previewed and say so. With an
  older Civil 3D plugin, which would ignore the preview flag, `doc_preview` refuses instead of running the tool.
- **Undo**: `doc_undo` takes back Claude's own last changes (`steps` for more than one), and only those. The
  plugin keeps a list of Claude's steps on top of Civil 3D's undo history for each drawing; when anything else
  changes the drawing (a command you run, Ctrl+Z, an edit in the Properties palette) or Claude uses the command
  line, a layout or a system variable, the list ends and `doc_undo` refuses and points you to Ctrl+Z. 183 tools.
- **Busy check**: while a dialog box is open in Civil 3D, a tool call now answers within two seconds with
  "Civil 3D is busy: the '...' dialog box is open" instead of waiting five minutes. While you are in the middle
  of a command (for example a LINE waiting for its next point), tools that would change the drawing are refused
  with the command's name; reading still works and your command keeps running.
- **Verify after write**: after every tool that changes the drawing, the plugin measures in the drawing itself
  what was added, changed and deleted (by type, with the new handles) and which settings changed (current layer,
  text style, linetype, LTSCALE...), then checks again after saving that new objects exist and deleted ones are
  gone. Claude gets one line, for example "Checked in the drawing after saving: added 1 Line (new handles D48)",
  or "nothing changed", or a WARNING when Civil 3D didn't keep a change.
- An unknown `CIVIL3D_MCP_MODE` now falls back to `full` with a warning in the MCP log.

### For contributors

- **CI**: GitHub Actions runs the Python tests (Python 3.10 and 3.13 on Windows) and `scripts/ci_checks.py`
  on every push and pull request: scripts stay ASCII and parse in Windows PowerShell 5.1, the release DLL holds
  no local folder paths, and `docs/tool-reference.md` matches the tools (`scripts/gen_tool_reference.py`).
  The C# plugin isn't built there (it needs Autodesk's DLLs); every tool is still matched to its C# handler.

### Fixed

- Every tool call, even one that only reads, left a step in Civil 3D's undo history (Civil 3D records one each
  time a program locks the drawing), so after Claude looked at the drawing you had to press Ctrl+Z several times
  to undo one change. Tools that only read or only send a command no longer lock the drawing, and the listing
  tools open it read-only; now each Ctrl+Z undoes one real change.
- A tool call made while a dialog box was open stayed queued after Claude had been told it timed out, and ran
  as soon as the dialog closed, so a retried change could happen twice. Calls that can't start are now taken
  back and never run.
- Tools could change the drawing in the middle of a command you were running, inside that command's undo step.
- A tool call that arrived while a command Claude had sent was still waiting to run (UNDO, ZOOM, the command
  line) could run first. The next call now waits until that exact command has run (Civil 3D's own hidden
  commands don't end the wait).

### Tested

Civil 3D 2025, two blank drawings: 12 live drawing-lock checks passed (after switching tabs, `drafting_line`,
`code_execute` and `doc_run_command` were refused and nothing was added; reads warned; after `doc_info` writes
worked again). 17 live preview checks passed: adding, deleting and moving lines, a new layer, a C# script and a
TIN surface were reported correctly and nothing was kept; save, command line and PDF plot were refused; a failing
call said so. Undo, live: one and two steps, a zoom and a preview in between, reads leaving no undo step, refusing after a
command-line command and after the user drew a polyline themselves (which was left alone): 12 checks, all
passed, also right after starting Civil 3D. The 66 security checks from 1.1.0 still pass. Verify after write, live: 10 checks passed (a line, a new current layer, a property change, a call that changed
nothing, a C# script, a read and a preview without a check line, deleting lines and a layer). Busy check, live: 9 checks passed (during a LINE command a read went through, a change and C# code were
refused at once and the LINE kept running; with the Text Style dialog open calls answered in 1.5 s; the refused
line never ran after the dialog closed). Search mode was run live (find, draw, measure,
delete) as well as in the automated tests.

## 1.1.0 (2026-11-19)

24 new tools (181 in total) and a security fix. Several ideas came from [Jjo37/new-acad](https://github.com/Jjo37/new-acad) (MIT).

**After updating:** close Civil 3D, run `install.bat` again, then fully quit and reopen Claude Desktop (or start a new
Claude Code session). An older MCP server can't talk to the new plugin ("Missing or wrong token").

### Security

Please update. In 1.0.0 a web page open in your browser could send commands to the plugin while Civil 3D was
running, and through `doc_run_command` start programs on your PC.

- **Secret token on every request.** The plugin makes a new random token each time it starts and saves it where only
  your Windows user can read it (`%LOCALAPPDATA%\Civil3DMcp\`). The MCP server sends it with every call. A request
  without it is refused, and a connection that doesn't start with a plain MCP request (such as a browser's HTTP
  request) is closed at once.
- **`doc_run_command`** refuses commands that start programs or open outside files (`SHELL`, `START`, `BROWSER`…),
  load code (`NETLOAD`, `APPLOAD`, `SCRIPT`, VBA), and `QUIT`. AutoLISP is refused unless Civil 3D is started with
  `CIVIL3D_MCP_ALLOW_LISP=1`.
- **`doc_set_sysvar`** can't change security settings (`SECURELOAD`, `TRUSTEDPATHS`, `TRUSTEDDOMAINS`…).
- **`doc_save`** only accepts a plain `.dwg` path. A path with quotes or line breaks could add commands.
- **Code check** now reads the names the C# compiler resolves, not just the text, so aliases and reflection no
  longer get around it. Scripts can't start programs, write, move or delete files, use the network, registry or
  native code, call methods through reflection or `dynamic`, send text to the command line, or reach the plugin's
  own classes. Reading files and all drawing work still work.
- **Tool labels**: every tool is marked read-only (58), adds objects only (78) or can change or delete existing
  objects (45), so Claude Desktop and Claude Code can ask before the risky ones.

### New

- **Sewer and storm design checks**: `pipes_hydraulics` (Manning full-flow capacity, velocity, normal depth and
  d/D for each pipe from design flows in L/s, plus a simplified hydraulic grade line and manhole freeboard) and
  `pipes_qc` (cover to the pipe's outer top, minimum and maximum slope, unconnected pipe ends, sumps above the
  lowest invert, rims that don't match the ground).
- **Hydrology**: `hydrology_rational` (Q = C·i·A, weighted C, areas or a boundary), `hydrology_time_of_concentration`
  (Kirpich, California / IRC:SP:13, FAA), `hydrology_catchment_area`, `hydrology_low_point`, `hydrology_flow_path`
  (water-drop path on a surface).
- **Pressure (water) networks**: `pressure_parts_lists`, `pressure_network_create`, `pressure_add_pipe` (depth
  from cover), `pressure_add_fitting`, `pressure_add_appurtenance`, `pressure_check_cover`. See the limitation below.
- **Stock subassemblies**: `assembly_list_stock`, `assembly_add_subassembly` (left, right or both sides, with
  parameters, hooked to another subassembly's point code such as ETW), `assembly_subassembly_params`,
  `assembly_info`. Corridors now get real cross sections.
- **Quantities and cost**: `qty_pipe_lengths` (pipe lengths by size and part counts, gravity and pressure),
  `qty_earthwork` (cut and fill of every volume surface), `qty_cost_estimate` (your own items and rates per part
  name applied to the pipe networks, optional CSV).
- **Road checks**: `qc_stopping_distance` (AASHTO or IRC), `qc_profile_k_values` (vertical curve K and length
  against the minimum for the design speed), `qc_alignment` (curve radii against the minimum for the speed).
- **`doc_summary`**: a short overview of a large drawing (object types, layers, extents, where things are,
  Civil objects, xrefs, layouts), so Claude doesn't have to list everything.
- **Big results are trimmed**: replies over 60,000 characters are shortened (long lists cut, with a note saying
  how many items were left out) instead of filling Claude's context. Set `CIVIL3D_MCP_MAX_RESULT_CHARS` to change it.
- `points_import` reads files that use a decimal comma (`1234,56`), detecting it automatically, and names the
  line when a value isn't a number.

### Fixed

- Pressure networks were never found by name: the lookup called an extension method as if it were an instance
  method. Every tool that takes a network name now finds pressure networks.
- `corridor_create` without a start and end ran past the end of the profile, where the corridor dropped to
  elevation 0 and inflated cut volumes. It now defaults to the stations the alignment and profile both cover.
- `corridor_add_surface` returned an empty surface; it now rebuilds the surface and reports its point count, or
  a warning when it has no points.
- The `import_csv` and `eg_from_csv` skill templates didn't compile: they called `ImportPoints` on the drawing's
  point collection, but in Civil 3D 2025 it is a static method (`CogoPointCollection.ImportPoints`).
- The plugin package declares Civil 3D 2025 and 2026 only. Civil 3D 2027 runs on .NET 10 and would fail to load
  this .NET 8 build.

### Known limitation

On Civil 3D 2025.0.1 with the stock metric pressure catalogs, Civil 3D refuses to add pressure pipes, fittings
and appurtenances, both through the API and in its own Pressure Network Creation Tools. The pressure tools now
explain this and suggest what to try (update Civil 3D, another pressure catalog). `pressure_check_cover`,
`pressure_network_info` and `qty_pipe_lengths` work on pressure networks drawn in Civil 3D.

### Tested

Civil 3D 2025 (Metric), blank drawing from the NCS template: 47 live checks of the new tools, all passed (for
example a 200 mm pipe at 1 % carries 32.8 L/s full, matching Manning by hand). Adding pressure parts could not be
tested (above); Civil 3D 2026 is not tested yet.

Security, live on Civil 3D 2025: 66 checks, all passed. A browser-style HTTP request, a request without a token and
one with a wrong token are refused; 8 dangerous commands (shell, LISP, NETLOAD, VBA, QUIT…) and 2 security
variables are blocked while normal commands and variables still work; a `doc_save` path with a line break is
refused; 18 unsafe scripts are refused (programs,
file writes, reflection, `dynamic`, network, registry, command line, the plugin's own classes) while 9 normal ones
run; all 18 skill templates compile and pass the code check.

## 1.0.0 (2026-11-14)

First public release.

### What's included

- **157 ready-made tools** for Civil 3D 2025 / 2026: drafting, modify, layers, annotation and all 9 dimension
  types, blocks, layouts and PDF plotting, COGO points and CSV import, surfaces (analysis, volumes), alignments,
  profiles, corridors, sections, gravity pipe networks and Civil styles.
- **Code execution**: Claude writes C# that runs inside Civil 3D with the full .NET API (`code_query` is read-only,
  `code_execute` commits), starting from a library of tested templates in `skills/`.
- **Three modes** to control token use: `full` (157 tools), `lean` (32) and `code` (8).
- **One-step installer** (`install.bat`): installs the plugin into the Civil 3D autoloader, creates a private
  Python environment, and connects Claude Desktop and Claude Code. Also `uninstall.bat`.
- **Install from Claude Code in one message**: "Install this repo: https://github.com/ctrlxbruce/Autocad-Civil3D-MCP"
  (instructions for the assistant are in `CLAUDE.md`).
- **`/civil3d` skill** for Claude Code: checks the connection, sums up the drawing, then works side by side.

### Fixed during release testing (Civil 3D 2025)

- `surface_analysis` always returned an empty list: it called an API method that doesn't exist in Civil 3D 2025.
  It now builds equal elevation or slope bands, saves them on the surface and returns them. Unknown `kind` values
  now raise an error instead of being ignored.
- `pipes_add_pipe` treated `start_invert` / `end_invert` as the pipe centreline, so inverts were off by half the
  pipe height. The reply now includes the inverts and whether the pipe falls from start to end.
- `pipes_add_pipe` connected pipes to structures before setting their elevations, so manhole sumps were computed
  from a pipe at elevation 0. Pipes are now connected after their inverts and slope are set.
- Pipe and structure tools no longer hide failures: settings that can't be applied come back as `warnings`, and
  a pipe left at elevation 0 is flagged.
- `pipes_interference_check` sent a command name that doesn't exist; it now starts `AeccCreateInterferenceCheck`.
- Code execution couldn't compile scripts that use `Pipe`, `Structure` or `Network` when they ran before Civil 3D
  had loaded its AEC base library. References are now refreshed when new assemblies load.
- The tool descriptions and docs named Civil 3D commands that don't exist (`SetNetworkCatalog`, `LandXMLIn`,
  `CreateSurfaceFromCorridor`) and the wrong ribbon tab for the pipe catalog. Corrected to `AeccSetNetworkCatalog`,
  `AeccLandXMLIn` and Home tab → Create Design → Pipe Network → Set Pipe Network Catalog.
- Release builds no longer embed the builder's local folder path in the DLL.
- The installer didn't connect current Microsoft Store builds of Claude Desktop, which keep their settings in
  `%LOCALAPPDATA%\Claude-Data\...`. It now writes there and skips folders that Claude Desktop has abandoned
  (marked `.user-data-left`). The uninstaller cleans all of them.
- Installing from a deeply nested folder failed while creating the Python environment, because some pip files
  passed Windows' 260-character path limit. The installer now falls back to `%LOCALAPPDATA%\Civil3DMcp\venv`,
  and the uninstaller removes that folder too.

### Tested

Civil 3D 2025 (Metric): 143 live tool calls across every group, gravity pipe networks in a drawing from the
Metric NCS template, the installer and uninstaller (sandbox and real PC), and Claude side by side with Civil 3D in
both the Claude Desktop chat and Claude Code.
Not yet tested: Civil 3D 2026, pressure networks, xrefs and DWG import. See the README's
[Known limitations](README.md#known-limitations).

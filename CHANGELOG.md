# Changelog

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

### Tested

Civil 3D 2025 (Metric): 143 live tool calls across every group, gravity pipe networks in a drawing from the
Metric NCS template, the installer and uninstaller (sandbox and real PC), and Claude side by side with Civil 3D in
both the Claude Desktop chat and Claude Code.
Not yet tested: Civil 3D 2026, pressure networks, xrefs and DWG import. See the README's
[Known limitations](README.md#known-limitations).

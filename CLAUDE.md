# Civil 3D MCP: notes for AI assistants

This repo connects Claude to Autodesk Civil 3D (Windows only): a Python MCP server (`mcp-server/`) talks to a
.NET 8 plugin (`civil3d-plugin/`, prebuilt in `release/`) that runs inside Civil 3D.

## If the user asks you to install this repo

Tell the user what you are doing at each step.

1. Get the project into `C:\civil3d-mcp` (if the user named another folder, use theirs everywhere below):
   - Folder doesn't exist: `git clone https://github.com/ctrlxbruce/Autocad-Civil3D-MCP.git C:\civil3d-mcp`
   - Folder exists and is a git checkout: `git -C C:\civil3d-mcp pull`
   - No git: download `https://github.com/ctrlxbruce/Autocad-Civil3D-MCP/archive/refs/heads/main.zip` and extract
     it so that `C:\civil3d-mcp\install.bat` exists.
2. Ask the user to close Civil 3D if it is open.
3. Run the installer. It does not ask questions and does not pause:

   ```
   powershell -NoProfile -ExecutionPolicy Bypass -File C:\civil3d-mcp\scripts\install.ps1
   ```

   Add `-Mode lean` (33 tools) or `-Mode code` (8 tools) only if the user wants fewer tools.
   Exit code 0 means everything worked. Otherwise read its last lines and help the user fix the problem.
4. If it stops with **"Python 3.10 or newer was not found"**, ask the user before installing Python
   (`winget install -e --id Python.Python.3.12`), then run step 3 again.
5. Tell the user what to do next:
   - Fully quit Claude Desktop (right-click its tray icon, then Quit) and open it again.
     In Claude Code, start a new session so the `civil3d` tools load.
   - Open Civil 3D with a drawing. The command line shows `[Civil3D MCP] Ready.`
   - Type **"Use Civil 3D"** in Claude (or `/civil3d` in Claude Code).

To update: `git -C C:\civil3d-mcp pull`, then run step 3 again.
To uninstall: close Civil 3D, then run
`powershell -NoProfile -ExecutionPolicy Bypass -File C:\civil3d-mcp\scripts\uninstall.ps1`.

## If the user asks you to use Civil 3D

Follow `claude-skill/civil3d/SKILL.md`: ping first, summarize the drawing, then work with the `civil3d` tools.

## Working on this repo

- Every tool needs a C# handler (`[Tool("group.name")]` in `civil3d-plugin/Handlers/`) and a Python catalogue
  entry (`mcp-server/tools/`). Check both sides with `python mcp-server/tests/test_server.py`.
- After changing C# code, rebuild the prebuilt DLL in `release/` so users get the change.
- Keep `.ps1` and `.bat` files ASCII-only. `.gitattributes` makes `.bat` / `.cmd` / `.ps1` files CRLF.
- `scripts/install.ps1` and `scripts/uninstall.ps1` must work in Windows PowerShell 5.1 (no `??`, `?:` or `&&`).

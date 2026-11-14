<#
.SYNOPSIS
    One-step installer for Civil 3D MCP (Windows).

.DESCRIPTION
    1. Checks that Civil 3D is closed.
    2. Installs the Civil 3D plugin into the autoloader, so Civil 3D loads it on every start.
    3. Creates a private Python environment (mcp-server\.venv) with the server's packages.
    4. Connects the "civil3d" MCP server to Claude Desktop and Claude Code (whichever are installed).
    5. Installs the "civil3d" skill for Claude Code (type /civil3d).
    Safe to run again: it updates an existing installation.

.PARAMETER Mode
    full (default, 157 tools), lean (32 tools) or code (8 tools).

.PARAMETER Python
    Path to a python.exe (3.10 or newer) to use instead of searching for one.

.PARAMETER PluginDll
    Plugin DLL to install. Default: release\Civil3DMcp.bundle\Contents\Civil3DMcp.dll

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install.ps1 -Mode lean
#>
[CmdletBinding()]
param(
    [ValidateSet('full', 'lean', 'code')]
    [string]$Mode = 'full',
    [string]$Python,
    [string]$PluginDll,
    [switch]$SkipClaudeDesktop,
    [switch]$SkipClaudeCode
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'civil3d-common.ps1')

$Root         = Split-Path -Parent $PSScriptRoot
$ServerPy     = Join-Path $Root 'mcp-server\server.py'
$Requirements = Join-Path $Root 'mcp-server\requirements.txt'
$VenvDir      = Join-Path $Root 'mcp-server\.venv'
$VenvPy       = Join-Path $VenvDir 'Scripts\python.exe'
$BundleSrc    = Join-Path $Root 'release\Civil3DMcp.bundle'
$PackageXml   = Join-Path $BundleSrc 'PackageContents.xml'
$SkillSrc     = Join-Path $Root 'claude-skill\civil3d\SKILL.md'
if (-not $PluginDll) { $PluginDll = Join-Path $BundleSrc 'Contents\Civil3DMcp.dll' }

$problems = New-Object System.Collections.Generic.List[string]

Write-Host ''
Write-Host 'Civil 3D MCP installer' -ForegroundColor White
Write-Host "Project folder: $Root"

try {
    foreach ($f in @($ServerPy, $Requirements, $PackageXml, $PluginDll, $SkillSrc)) {
        if (-not (Test-Path -LiteralPath $f)) { throw "Missing file: $f`n    Download the complete project again." }
    }

    # ---------------------------------------------------------------- 1. Civil 3D
    Write-Step '1/5  Checking Civil 3D'
    if (Test-Civil3DRunning) {
        throw 'Civil 3D (or AutoCAD) is running. Close it, then run the installer again.'
    }
    $versions = @('2025', '2026') | Where-Object {
        Test-Path -LiteralPath (Join-Path $env:ProgramFiles "Autodesk\AutoCAD $_\C3D")
    }
    if ($versions) {
        Write-Ok ('Civil 3D found: ' + ($versions -join ', '))
    } else {
        Write-Note 'Civil 3D 2025/2026 was not found. The plugin is installed anyway and loads once Civil 3D is installed.'
    }

    # ---------------------------------------------------------------- 2. Plugin
    Write-Step '2/5  Installing the Civil 3D plugin'
    $bundle = Get-BundleDir
    $contents = Join-Path $bundle 'Contents'
    try {
        New-Item -ItemType Directory -Force -Path $contents | Out-Null
        Copy-Item -LiteralPath $PackageXml -Destination (Join-Path $bundle 'PackageContents.xml') -Force
        Copy-Item -LiteralPath $PluginDll -Destination (Join-Path $contents 'Civil3DMcp.dll') -Force
    } catch {
        throw "Could not copy the plugin into $bundle. Make sure Civil 3D is closed, then run the installer again."
    }
    Unblock-File -LiteralPath (Join-Path $contents 'Civil3DMcp.dll') -ErrorAction SilentlyContinue
    Write-Ok "Plugin installed: $bundle"

    # ---------------------------------------------------------------- 3. Python
    Write-Step '3/5  Setting up Python'
    $venvOk = $false
    if ((-not $Python) -and (Test-Path -LiteralPath $VenvPy)) {
        $venvOk = ((Invoke-Quiet $VenvPy @('-c', 'import sys')).Code -eq 0)
    }
    if ($venvOk) {
        Write-Ok 'Python environment already exists'
    } else {
        if (Test-Path -LiteralPath $VenvDir) { Remove-Item -LiteralPath $VenvDir -Recurse -Force }
        $py = Find-Python $Python
        if (-not $py) {
            throw ("Python 3.10 or newer was not found.`n" +
                   "    Install it from https://www.python.org/downloads/ (tick 'Add python.exe to PATH'),`n" +
                   "    or run:  winget install -e --id Python.Python.3.12`n" +
                   "    Then run this installer again.")
        }
        Write-Ok "Using Python $($py.Version): $($py.Exe)"
        Invoke-Checked $py.Exe @('-m', 'venv', $VenvDir) 'Could not create the Python environment (mcp-server\.venv).'
    }
    Write-Host '    Installing Python packages (the first time can take a minute)...'
    Invoke-Checked $VenvPy @('-m', 'pip', 'install', '--disable-pip-version-check', '--quiet', '-r', $Requirements) `
        'Could not install the Python packages. Check your internet connection, then run the installer again.'
    Invoke-Checked $VenvPy @('-c', 'import mcp, anyio') 'The Python packages were installed but cannot be loaded.'
    Write-Ok "Python ready: $VenvPy"

    # ---------------------------------------------------------------- 4. Claude
    Write-Step '4/5  Connecting Claude'
    $entry = [pscustomobject][ordered]@{
        command = $VenvPy
        args    = @($ServerPy)
        env     = [pscustomobject]@{ CIVIL3D_MCP_MODE = $Mode }
    }
    $connected = 0

    if ($SkipClaudeDesktop) {
        Write-Note 'Claude Desktop skipped (-SkipClaudeDesktop).'
    } else {
        $dirs = Get-ClaudeDesktopConfigDirs
        if ($dirs.Count -eq 0) {
            Write-Note 'Claude Desktop not found, skipped. Install it from https://claude.ai/download, then run this installer again.'
        }
        foreach ($d in $dirs) {
            $cfgPath = Join-Path $d 'claude_desktop_config.json'
            try {
                Set-ClaudeDesktopServer $cfgPath $entry
                Write-Ok "Claude Desktop: $cfgPath"
                $connected++
            } catch {
                Write-Bad $_.Exception.Message
                $problems.Add("Claude Desktop config not updated: $cfgPath")
            }
        }
    }

    $claude = $null
    if ($SkipClaudeCode) {
        Write-Note 'Claude Code skipped (-SkipClaudeCode).'
    } else {
        $claude = Get-ClaudeCli
        if (-not $claude) {
            Write-Note 'Claude Code not found, skipped.'
        } else {
            Invoke-Quiet $claude @('mcp', 'remove', $ServerName, '--scope', 'user') | Out-Null
            $r = Invoke-Quiet $claude @('mcp', 'add', $ServerName, '--scope', 'user',
                                        '--env', "CIVIL3D_MCP_MODE=$Mode", '--', $VenvPy, $ServerPy)
            if ($r.Code -eq 0) {
                Write-Ok "Claude Code: added '$ServerName' for your user"
                $connected++
            } else {
                Write-Bad 'Could not add the server to Claude Code.'
                $problems.Add('Claude Code not connected')
            }
        }
    }
    if ($connected -eq 0 -and $problems.Count -eq 0) {
        $problems.Add('No Claude app was connected. Install Claude Desktop or Claude Code, then run this installer again.')
    }

    # ---------------------------------------------------------------- 5. Skill
    Write-Step '5/5  Installing the Claude Code skill'
    $skillDir = Get-SkillDir
    $claudeHome = $null
    if ($env:USERPROFILE) { $claudeHome = Join-Path $env:USERPROFILE '.claude' }
    if ($SkipClaudeCode) {
        Write-Note 'Skipped (-SkipClaudeCode).'
    } elseif (-not $claude -and -not ($claudeHome -and (Test-Path -LiteralPath $claudeHome))) {
        Write-Note 'Claude Code not found, skipped.'
    } else {
        New-Item -ItemType Directory -Force -Path $skillDir | Out-Null
        Copy-Item -LiteralPath $SkillSrc -Destination (Join-Path $skillDir 'SKILL.md') -Force
        Write-Ok "Skill installed: $skillDir  (type /civil3d in Claude Code)"
    }
} catch {
    Write-Host ''
    Write-Bad $_.Exception.Message
    Write-Host ''
    Write-Host 'Installation stopped. Fix the problem above, then run the installer again.' -ForegroundColor Red
    exit 1
}

Write-Host ''
if ($problems.Count -gt 0) {
    Write-Host 'Installed, but with problems:' -ForegroundColor Yellow
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Yellow }
} else {
    Write-Host 'Civil 3D MCP is installed.' -ForegroundColor Green
}
Write-Host ''
Write-Host 'Next steps:'
Write-Host '  1. Fully quit Claude Desktop (right-click its tray icon > Quit) and open it again.'
Write-Host '     In Claude Code, start a new session.'
Write-Host '  2. Start Civil 3D and open a drawing. The command line shows: [Civil3D MCP] Ready.'
Write-Host '  3. In Claude, type:  Use Civil 3D'
Write-Host ''
if ($problems.Count -gt 0) { exit 1 }
exit 0

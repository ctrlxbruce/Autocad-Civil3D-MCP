<#
.SYNOPSIS
    Removes Civil 3D MCP (Windows).

.DESCRIPTION
    Removes the Civil 3D plugin, the "civil3d" entry from Claude Desktop and Claude Code,
    the /civil3d skill and the Python environment (mcp-server\.venv).
    Your other Claude settings and MCP servers are kept.

.PARAMETER KeepPython
    Keep the Python environment (mcp-server\.venv).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\uninstall.ps1
#>
[CmdletBinding()]
param(
    [switch]$KeepPython
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'civil3d-common.ps1')

$Root    = Split-Path -Parent $PSScriptRoot
$VenvDir = Join-Path $Root 'mcp-server\.venv'
$problems = New-Object System.Collections.Generic.List[string]

Write-Host ''
Write-Host 'Civil 3D MCP uninstaller' -ForegroundColor White

try {
    # ---------------------------------------------------------------- 1. Plugin
    Write-Step '1/4  Removing the Civil 3D plugin'
    if (Test-Civil3DRunning) {
        throw 'Civil 3D (or AutoCAD) is running. Close it, then run the uninstaller again.'
    }
    $bundle = Get-BundleDir
    if (Test-Path -LiteralPath $bundle) {
        Remove-Item -LiteralPath $bundle -Recurse -Force
        Write-Ok "Removed $bundle"
    } else {
        Write-Ok 'The plugin was not installed'
    }

    # ---------------------------------------------------------------- 2. Claude
    Write-Step '2/4  Disconnecting Claude'
    foreach ($d in (Get-ClaudeDesktopConfigDirs -IncludeAbandoned)) {
        $cfgPath = Join-Path $d 'claude_desktop_config.json'
        try {
            if (Remove-ClaudeDesktopServer $cfgPath) { Write-Ok "Claude Desktop: removed from $cfgPath" }
        } catch {
            Write-Bad $_.Exception.Message
            $problems.Add("Claude Desktop config not updated: $cfgPath")
        }
    }
    $claude = Get-ClaudeCli
    if ($claude) {
        $r = Invoke-Quiet $claude @('mcp', 'remove', $ServerName, '--scope', 'user')
        if ($r.Code -eq 0) { Write-Ok "Claude Code: removed '$ServerName'" }
        else { Write-Ok "Claude Code: '$ServerName' was not registered" }
    }

    # ---------------------------------------------------------------- 3. Skill
    Write-Step '3/4  Removing the Claude Code skill'
    $skillDir = Get-SkillDir
    $skillFile = $null
    if ($skillDir) { $skillFile = Join-Path $skillDir 'SKILL.md' }
    if ($skillFile -and (Test-Path -LiteralPath $skillFile) -and
        (Select-String -LiteralPath $skillFile -SimpleMatch $SkillMarker -Quiet)) {
        Remove-Item -LiteralPath $skillDir -Recurse -Force
        Write-Ok "Removed $skillDir"
    } else {
        Write-Ok 'The skill was not installed'
    }

    # ---------------------------------------------------------------- 4. Python
    Write-Step '4/4  Removing the Python environment'
    $venvDirs = @($VenvDir)
    if ($env:LOCALAPPDATA) { $venvDirs += (Join-Path $env:LOCALAPPDATA 'Civil3DMcp\venv') }
    if ($KeepPython) {
        Write-Note 'Kept (-KeepPython).'
    } else {
        $removedAny = $false
        foreach ($d in $venvDirs) {
            if (-not (Test-Path -LiteralPath $d)) { continue }
            try {
                Remove-Folder $d
                Write-Ok "Removed $d"
                $removedAny = $true
            } catch {
                Write-Bad $_.Exception.Message
                $problems.Add('Python environment not removed')
            }
        }
        if ($env:LOCALAPPDATA) {
            $own = Join-Path $env:LOCALAPPDATA 'Civil3DMcp'
            if ((Test-Path -LiteralPath $own) -and -not (Get-ChildItem -LiteralPath $own -Force)) { Remove-Item -LiteralPath $own -Force }
        }
        if (-not $removedAny -and $problems.Count -eq 0) { Write-Ok 'Nothing to remove' }
    }
} catch {
    Write-Host ''
    Write-Bad $_.Exception.Message
    Write-Host ''
    Write-Host 'Uninstall stopped. Fix the problem above, then run the uninstaller again.' -ForegroundColor Red
    exit 1
}

Write-Host ''
if ($problems.Count -gt 0) {
    Write-Host 'Removed, but with problems:' -ForegroundColor Yellow
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Yellow }
} else {
    Write-Host 'Civil 3D MCP was removed.' -ForegroundColor Green
}
Write-Host ''
Write-Host 'Next steps:'
Write-Host '  1. Fully quit Claude Desktop and open it again.'
Write-Host "  2. You can now delete the project folder: $Root"
Write-Host ''
if ($problems.Count -gt 0) { exit 1 }
exit 0

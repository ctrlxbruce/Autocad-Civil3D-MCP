# Shared helpers for install.ps1 and uninstall.ps1 (Civil 3D MCP).
# Keep this file ASCII-only: Windows PowerShell 5.1 reads scripts without a BOM as ANSI.

$ServerName  = 'civil3d'
$BundleName  = 'Civil3DMcp.bundle'
$SkillMarker = 'Installed by Civil 3D MCP'

function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text)   { Write-Host "    [OK] $Text" -ForegroundColor Green }
function Write-Note([string]$Text) { Write-Host "    [!]  $Text" -ForegroundColor Yellow }
function Write-Bad([string]$Text)  { Write-Host "    [X]  $Text" -ForegroundColor Red }

# %APPDATA%\Autodesk\ApplicationPlugins\Civil3DMcp.bundle (the Civil 3D autoloader folder for this plugin)
function Get-BundleDir {
    if ([string]::IsNullOrEmpty($env:APPDATA)) { throw 'The APPDATA environment variable is not set.' }
    return (Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\$BundleName")
}

# %USERPROFILE%\.claude\skills\civil3d (Claude Code personal skill folder)
function Get-SkillDir {
    if ([string]::IsNullOrEmpty($env:USERPROFILE)) { return $null }
    return (Join-Path $env:USERPROFILE ".claude\skills\$ServerName")
}

# Deletes one of the installer's own folders, including files whose full path is longer than
# Windows' 260-character limit (Remove-Item can't delete those, the \\?\ form of the path can).
function Remove-Folder([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try { Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop }
    catch { try { [System.IO.Directory]::Delete('\\?\' + $Path, $true) } catch { } }
    if (Test-Path -LiteralPath $Path) {
        throw "Could not delete $Path. Quit Claude Desktop and Claude Code, then try again."
    }
}

function Test-Civil3DRunning {
    return [bool](Get-Process -Name 'acad' -ErrorAction SilentlyContinue)
}

# Runs a program without letting its error output stop the script.
# Returns the exit code and the non-empty lines it printed.
function Invoke-Quiet([string]$Exe, [string[]]$Arguments) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $Exe @Arguments 2>$null
        $code = $LASTEXITCODE
        $lines = @($out | ForEach-Object { "$_".Trim() } | Where-Object { $_ -ne '' })
        return [pscustomobject]@{ Code = $code; Lines = $lines }
    } catch {
        return [pscustomobject]@{ Code = -1; Lines = @() }
    } finally {
        $ErrorActionPreference = $prev
    }
}

# Runs a program and shows its output. Throws $Message if it fails.
function Invoke-Checked([string]$Exe, [string[]]$Arguments, [string]$Message) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments
        $code = $LASTEXITCODE
    } catch {
        $code = -1
    } finally {
        $ErrorActionPreference = $prev
    }
    if ($code -ne 0) { throw $Message }
}

# Finds Python 3.10 or newer. Returns @{ Exe; Version } or $null.
# Tries -Python, then the py launcher, then python / python3. The Microsoft Store
# "python" shortcut is skipped automatically because it does not run the probe.
function Find-Python([string]$Preferred) {
    $probe = 'import sys; print(sys.executable); print(sys.version_info[0]); print(sys.version_info[1])'
    $candidates = @()
    if ($Preferred) { $candidates += , @($Preferred) }
    $candidates += , @('py', '-3')
    $candidates += , @('python')
    $candidates += , @('python3')
    foreach ($c in $candidates) {
        $exe = $c[0]
        $pre = @()
        if ($c.Count -gt 1) { $pre = @($c[1..($c.Count - 1)]) }
        $r = Invoke-Quiet $exe ($pre + @('-c', $probe))
        if ($r.Code -ne 0 -or $r.Lines.Count -lt 3) { continue }
        $n = $r.Lines.Count
        $major = 0
        $minor = 0
        if (-not [int]::TryParse($r.Lines[$n - 2], [ref]$major)) { continue }
        if (-not [int]::TryParse($r.Lines[$n - 1], [ref]$minor)) { continue }
        $path = $r.Lines[$n - 3]
        $okVersion = ($major -gt 3) -or ($major -eq 3 -and $minor -ge 10)
        if ($okVersion -and (Test-Path -LiteralPath $path)) {
            return [pscustomobject]@{ Exe = $path; Version = "$major.$minor" }
        }
    }
    return $null
}

# Every folder Claude Desktop may read claude_desktop_config.json from:
#   - %LOCALAPPDATA%\Claude-Data\<package>   (current Microsoft Store builds)
#   - the MSIX package's LocalCache\Roaming\Claude folder (older Store builds)
#   - %APPDATA%\Claude                        (classic installer)
# A folder holding a ".user-data-left*" marker was abandoned by Claude Desktop (its data moved elsewhere),
# so it is skipped unless -IncludeAbandoned is given (the uninstaller uses that to clean up old entries).
function Get-ClaudeDesktopConfigDirs([switch]$IncludeAbandoned) {
    $dirs = New-Object System.Collections.Generic.List[string]
    if ($env:LOCALAPPDATA) {
        $claudeData = Join-Path $env:LOCALAPPDATA 'Claude-Data'
        if (Test-Path -LiteralPath $claudeData) {
            foreach ($d in (Get-ChildItem -LiteralPath $claudeData -Directory -ErrorAction SilentlyContinue)) {
                if ($d.Name -like '*claude*') { $dirs.Add($d.FullName) }
            }
        }
        $packages = Join-Path $env:LOCALAPPDATA 'Packages'
        if (Test-Path -LiteralPath $packages) {
            foreach ($p in (Get-ChildItem -LiteralPath $packages -Directory -ErrorAction SilentlyContinue)) {
                if ($p.Name -like '*Claude_*') {
                    $d = Join-Path $p.FullName 'LocalCache\Roaming\Claude'
                    if (Test-Path -LiteralPath $d) { $dirs.Add($d) }
                }
            }
        }
    }
    if ($env:APPDATA) {
        $classic = Join-Path $env:APPDATA 'Claude'
        if (Test-Path -LiteralPath $classic) { $dirs.Add($classic) }
    }
    $result = @($dirs | Where-Object {
        $IncludeAbandoned -or -not (Get-ChildItem -LiteralPath $_ -Force -Filter '.user-data-left*' -ErrorAction SilentlyContinue)
    } | Select-Object -Unique)
    return , [string[]]$result
}

function Get-ClaudeCli {
    $c = Get-Command 'claude' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($c) { return $c.Source }
    return $null
}

# Reads a JSON file that must hold an object. Returns $null if the file does not exist.
function Read-JsonObject([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $raw = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) { return [pscustomobject]@{} }
    try {
        $obj = $raw | ConvertFrom-Json
    } catch {
        throw "Could not read $Path because it is not valid JSON. Fix or delete that file, then run this again."
    }
    if ($obj -isnot [System.Management.Automation.PSCustomObject]) {
        throw "Could not read $Path because it does not contain a JSON object."
    }
    return $obj
}

# Writes UTF-8 without a BOM (Claude Desktop cannot read a BOM).
function Write-JsonObject([string]$Path, $Object) {
    $json = $Object | ConvertTo-Json -Depth 32
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Backup-File([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Copy-Item -LiteralPath $Path -Destination "$Path.civil3d-backup" -Force
    }
}

# Adds or replaces mcpServers.civil3d and keeps everything else in the file.
function Set-ClaudeDesktopServer([string]$ConfigPath, $Entry) {
    $cfg = Read-JsonObject $ConfigPath
    if ($null -eq $cfg) { $cfg = [pscustomobject]@{} }
    $servers = $null
    if ($cfg.PSObject.Properties['mcpServers']) { $servers = $cfg.mcpServers }
    if ($servers -isnot [System.Management.Automation.PSCustomObject]) {
        $servers = [pscustomobject]@{}
        $cfg | Add-Member -NotePropertyName 'mcpServers' -NotePropertyValue $servers -Force
    }
    $servers | Add-Member -NotePropertyName $ServerName -NotePropertyValue $Entry -Force
    Backup-File $ConfigPath
    Write-JsonObject $ConfigPath $cfg
}

# Removes mcpServers.civil3d and keeps everything else. Returns $true if something was removed.
function Remove-ClaudeDesktopServer([string]$ConfigPath) {
    $cfg = Read-JsonObject $ConfigPath
    if ($null -eq $cfg -or -not $cfg.PSObject.Properties['mcpServers']) { return $false }
    $servers = $cfg.mcpServers
    if ($servers -isnot [System.Management.Automation.PSCustomObject]) { return $false }
    if (-not $servers.PSObject.Properties[$ServerName]) { return $false }
    Backup-File $ConfigPath
    $servers.PSObject.Properties.Remove($ServerName)
    Write-JsonObject $ConfigPath $cfg
    return $true
}

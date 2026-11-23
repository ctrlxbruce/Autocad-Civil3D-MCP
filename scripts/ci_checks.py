"""Repository checks that don't need Civil 3D. CI runs them on every push; run them yourself before a release:

    python scripts/ci_checks.py

1. .ps1 / .bat / .cmd files are ASCII only (Windows PowerShell 5.1 misreads UTF-8 without a BOM).
2. Every .ps1 parses in Windows PowerShell 5.1 (skipped where powershell.exe doesn't exist).
3. The prebuilt plugin DLL in release/ holds no local folder paths or debug-symbol paths.
4. docs/tool-reference.md matches the tool catalogue.
"""
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DLL = ROOT / "release" / "Civil3DMcp.bundle" / "Contents" / "Civil3DMcp.dll"
# Signs of a builder's machine in a binary: user folders and .pdb paths (Release builds use DebugType=none + PathMap).
LEAKS = ["\\Users\\", "/Users/", "/home/", ".pdb"]
failures = []


def fail(msg):
    failures.append(msg)
    print("FAIL", msg)


def scripts():
    return sorted(p for ext in ("*.ps1", "*.bat", "*.cmd") for p in ROOT.rglob(ext)
                  if not any(part in (".git", ".venv", "bin", "obj") for part in p.parts))


# 1. ASCII only
for p in scripts():
    data = p.read_bytes()
    bad = next((i for i, b in enumerate(data) if b > 127), None)
    if bad is not None:
        line = data[:bad].count(b"\n") + 1
        fail(f"{p.relative_to(ROOT)} line {line}: non-ASCII character (keep .ps1/.bat/.cmd files ASCII)")
print("checked ASCII in", len(scripts()), "scripts")

# 2. PowerShell 5.1 parse
ps = shutil.which("powershell.exe") or shutil.which("powershell")
if ps:
    for p in scripts():
        if p.suffix != ".ps1":
            continue
        cmd = ("$t = $null; $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile("
               f"'{p}', [ref]$t, [ref]$e); if ($e.Count) {{ $e | ForEach-Object {{ $_.ToString() }}; exit 1 }}")
        r = subprocess.run([ps, "-NoProfile", "-NonInteractive", "-Command", cmd], capture_output=True, text=True)
        if r.returncode != 0:
            fail(f"{p.relative_to(ROOT)} doesn't parse in Windows PowerShell 5.1: {(r.stdout + r.stderr).strip()[:300]}")
    print("parsed .ps1 files with", ps)
else:
    print("skipped PowerShell parse check (no powershell.exe here)")

# 3. Release DLL
if DLL.exists():
    data = DLL.read_bytes()
    for pat in LEAKS:
        for enc in ("ascii", "utf-16-le"):
            if pat.encode(enc) in data:
                fail(f"release DLL contains '{pat}' ({enc}): rebuild in Release (DebugType=none, PathMap) before committing")
    print("scanned", DLL.relative_to(ROOT), f"({len(data)} bytes)")
else:
    fail(f"{DLL.relative_to(ROOT)} is missing")

# 4. Tool reference
r = subprocess.run([sys.executable, str(ROOT / "scripts" / "gen_tool_reference.py"), "--check"],
                   capture_output=True, text=True)
print((r.stdout + r.stderr).strip())
if r.returncode != 0:
    fail("docs/tool-reference.md is out of date")

if failures:
    print(f"\n{len(failures)} check(s) failed")
    sys.exit(1)
print("\nall checks passed")

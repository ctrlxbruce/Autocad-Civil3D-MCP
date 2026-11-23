"""Write docs/tool-reference.md from the MCP tool catalogue, or check that it is up to date.

    python scripts/gen_tool_reference.py           # rewrite the file
    python scripts/gen_tool_reference.py --check   # exit 1 if it is out of date (used by CI)
"""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "mcp-server"))

from tools import CODE_MODE, LEAN_MODE, all_tools  # noqa: E402
from tools import (code, core, preview, drafting, modify, layers, annotation, blocks, layouts,  # noqa: E402
                   civil_points, civil_surfaces, civil_alignments, civil_corridors, civil_pipes, civil_pressure,
                   civil_hydraulics, civil_quantities, civil_styles)

OUT = ROOT / "docs" / "tool-reference.md"
SECTIONS = [
    (code, "Code execution & skills"), (core, "Core, query, properties, fallback"),
    (preview, "Preview (dry run)"), (drafting, "Drafting"),
    (modify, "Modify"), (layers, "Layers & linetypes"), (annotation, "Text, dimensions, leaders, styles"),
    (blocks, "Blocks & xrefs"), (layouts, "Layouts & plotting"), (civil_points, "COGO points & point groups"),
    (civil_surfaces, "Surfaces"), (civil_alignments, "Alignments & profiles"),
    (civil_corridors, "Assemblies, corridors, sections, grading, parcels"), (civil_pipes, "Pipe networks"),
    (civil_pressure, "Pressure (water supply) networks"),
    (civil_hydraulics, "Pipe hydraulics, network QC and hydrology"),
    (civil_quantities, "Quantities, cost estimate and road QC"), (civil_styles, "Civil objects & styles"),
]


def render() -> str:
    total = len(all_tools("full"))
    out = [
        "# Tool reference", "",
        f"**{total} tools** in `full` mode. In Claude the dots become underscores (`drafting.line` → `drafting_line`).", "",
        "Points are `[x, y]` or `[x, y, z]`. Angles are degrees, except in `props_set`, `props_call` and C# code, which use radians.", "",
        "Mode column: **C** = loaded in `code` mode, **L** = loaded in `lean` mode, all tools load in `full` mode.",
        "`search` mode lists a few of them plus `tools_search`, `tools_read` and `tools_call`, which reach every tool "
        "below by name.", "",
    ]
    seen = 0
    for module, title in SECTIONS:
        items = list(module.TOOLS.items()) + list(getattr(module, "LOCAL_TOOLS", {}).items())
        out += [f"## {title}", "", "| Tool | Mode | What it does | Inputs (**bold** = required) |", "|---|---|---|---|"]
        for name, (desc, params) in items:
            mode = "C" if name in CODE_MODE else ("L" if name in LEAN_MODE else "")
            inputs = ", ".join(f"**{p}**" if len(s) > 2 and s[2] else p for p, s in params.items())
            text = " ".join(desc.split()).replace("|", "\\|")
            out.append(f"| `{name.replace('.', '_')}` | {mode} | {text} | {inputs} |")
            seen += 1
        out.append("")
    if seen != total:
        raise SystemExit(f"listed {seen} of {total} tools: add the new tool module to SECTIONS")
    return "\n".join(out)


if __name__ == "__main__":
    text = render()
    if "--check" in sys.argv:
        current = OUT.read_text(encoding="utf-8").replace("\r\n", "\n") if OUT.exists() else ""
        if current != text:
            print("docs/tool-reference.md is out of date: run  python scripts/gen_tool_reference.py")
            sys.exit(1)
        print("docs/tool-reference.md is up to date")
    else:
        OUT.write_text(text, encoding="utf-8", newline="\n")
        print(f"wrote {OUT.relative_to(ROOT)}")

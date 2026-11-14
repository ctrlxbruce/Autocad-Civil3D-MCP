"""Reads the C# skill templates in ../skills (*.skill.md). No Civil 3D needed."""
from __future__ import annotations

import os
import re
from pathlib import Path

SKILLS_DIR = Path(os.environ.get("CIVIL3D_SKILLS_DIR", Path(__file__).resolve().parent.parent / "skills"))


def _load() -> list[dict]:
    out = []
    for f in sorted(SKILLS_DIR.rglob("*.skill.md")):
        text = f.read_text(encoding="utf-8")
        meta = {}
        m = re.match(r"---\n(.*?)\n---\n", text, re.S)
        if m:
            for line in m.group(1).splitlines():
                if ":" in line:
                    k, v = line.split(":", 1)
                    meta[k.strip()] = v.strip()
        out.append({
            "name": meta.get("name", f.stem.replace(".skill", "")),
            "category": meta.get("category", f.parent.name),
            "description": meta.get("description", ""),
            "tags": meta.get("tags", "").strip("[]"),
            "mode": meta.get("mode", "execute"),
            "path": f,
        })
    return out


def list_skills(category: str | None = None) -> list[dict]:
    return [{k: s[k] for k in ("name", "category", "description", "mode")}
            for s in _load() if not category or s["category"] == category]


def search(query: str) -> list[dict]:
    words = [w.lower() for w in re.findall(r"\w+", query)]
    scored = []
    for s in _load():
        hay = f"{s['name']} {s['category']} {s['description']} {s['tags']}".lower()
        score = sum(hay.count(w) for w in words)
        if score:
            scored.append((score, s))
    scored.sort(key=lambda x: -x[0])
    return [{k: s[k] for k in ("name", "category", "description", "mode")} for _, s in scored[:10]]


def read(name: str) -> str:
    name = name.replace(".skill.md", "")
    for s in _load():
        if name in (s["name"], f"{s['category']}/{s['name']}"):
            return s["path"].read_text(encoding="utf-8")
    raise KeyError(f"No skill '{name}'. Use skills_list or skills_search.")


def call(tool: str, args: dict) -> object:
    if tool == "skills.list":
        return list_skills(args.get("category"))
    if tool == "skills.search":
        return search(args["query"])
    if tool == "skills.read":
        return read(args["name"])
    raise KeyError(tool)

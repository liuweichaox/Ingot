#!/usr/bin/env python3
"""Enforce bilingual structure and restrained public documentation language."""
from __future__ import annotations

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]
PAIRS = [
    (ROOT / "README.md", ROOT / "README.en.md"),
    (ROOT / "CONTRIBUTING.md", ROOT / "CONTRIBUTING.en.md"),
    (ROOT / "optimizer" / "README.md", ROOT / "optimizer" / "README.en.md"),
]
PAIRS.extend(
    (path, path.with_name(f"{path.stem}.en.md"))
    for path in sorted((ROOT / "docs").glob("*.md"))
    if not path.name.endswith(".en.md")
)

PUBLIC_DOCUMENTS = [
    ROOT / "README.md",
    ROOT / "README.en.md",
    *sorted((ROOT / "docs").glob("*.md")),
    ROOT / "optimizer" / "README.md",
    ROOT / "optimizer" / "README.en.md",
]

FORBIDDEN = {
    "zh": re.compile(
        r"优化大脑|智能大脑|AI[ -]?赋能|颠覆(?:性)?|革命性|一键(?:生成)?最优|"
        r"保证(?:减少|缩短)|必然(?:减少|缩短)|零试验"
    ),
    "en": re.compile(
        r"optimization brain|intelligent brain|AI-powered|revolutionary|"
        r"one-click optimum|"
        r"guaranteed (?:experiment|development-time) reduction|zero experiments",
        re.IGNORECASE,
    ),
}


def heading_levels(path: Path) -> list[int]:
    levels = []
    in_fence = False
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("```"):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        match = re.match(r"^(#{1,6})\s+\S", line)
        if match:
            levels.append(len(match.group(1)))
    return levels


def main() -> int:
    failures = []
    for left, right in PAIRS:
        if not right.exists():
            failures.append(f"missing English document for {left.relative_to(ROOT)}")
            continue
        if heading_levels(left) != heading_levels(right):
            failures.append(
                f"heading structure differs: {left.relative_to(ROOT)} and {right.relative_to(ROOT)}"
            )
    for path in PUBLIC_DOCUMENTS:
        text = path.read_text(encoding="utf-8")
        pattern = FORBIDDEN["en" if path.name.endswith(".en.md") else "zh"]
        match = pattern.search(text)
        if match:
            line = text.count("\n", 0, match.start()) + 1
            failures.append(
                f"promotional or unsupported wording in {path.relative_to(ROOT)}:{line}: {match.group(0)}"
            )
    if failures:
        print("Documentation style verification failed:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1
    print("Documentation language and bilingual structure verified.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

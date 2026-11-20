from __future__ import annotations

import ast
from pathlib import Path


def test_public_optimizer_api_has_docstrings() -> None:
    package_root = Path(__file__).parents[1] / "ingot_optimizer"
    missing: list[str] = []
    for path in sorted(package_root.glob("*.py")):
        module = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        for node in module.body:
            if not isinstance(
                node,
                (ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef),
            ) or node.name.startswith("_"):
                continue
            if ast.get_docstring(node) is None:
                missing.append(f"{path.name}:{node.lineno}:{node.name}")

    assert missing == [], "公共 Optimizer API 缺少 Docstring：\n" + "\n".join(missing)

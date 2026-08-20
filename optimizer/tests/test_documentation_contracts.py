"""Verify documentation contracts success, rejection, and boundary behavior."""

from __future__ import annotations

import ast
from pathlib import Path


def test_python_files_have_module_docstrings() -> None:
    optimizer_root = Path(__file__).parents[1]
    missing: list[str] = []
    for root in (optimizer_root / "ingot_optimizer", optimizer_root / "tests"):
        for path in sorted(root.glob("*.py")):
            module = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
            if ast.get_docstring(module) is None:
                missing.append(str(path.relative_to(optimizer_root)))

    assert missing == [], "Python 文件缺少模块 Docstring：\n" + "\n".join(missing)


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

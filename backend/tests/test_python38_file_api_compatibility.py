"""Windows 7 随包 Python 3.8 的文件 API 兼容门禁。"""

from __future__ import annotations

import ast
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def test_runtime_code_does_not_pass_newline_to_path_write_text() -> None:
    """禁止使用 Python 3.10 才加入的 Path.write_text(newline=...)。"""

    violations: list[tuple[str, int]] = []
    for source_root in (ROOT / "backend/app", ROOT / "scripts", ROOT / "packaging"):
        for source_path in sorted(source_root.rglob("*.py")):
            tree = ast.parse(source_path.read_text(encoding="utf-8"))
            for node in ast.walk(tree):
                if not isinstance(node, ast.Call):
                    continue
                if not isinstance(node.func, ast.Attribute):
                    continue
                if node.func.attr != "write_text":
                    continue
                if any(keyword.arg == "newline" for keyword in node.keywords):
                    violations.append(
                        (source_path.relative_to(ROOT).as_posix(), node.lineno)
                    )

    assert violations == []

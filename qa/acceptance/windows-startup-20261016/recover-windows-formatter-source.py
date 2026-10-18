"""从可验证的 Git 历史恢复 Windows 固定快照；不修改当前 Mac 源码。"""
from __future__ import annotations

import importlib.util
import json
import shutil
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[3]
source = root / "vendor/document-formatter-source"
destination = root / ".qa/windows-formatter-upstream"
spec = importlib.util.spec_from_file_location("formatter_snapshot", root / "scripts/verify-document-formatter-source-snapshot.py")
snapshot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(snapshot)
snapshot.verify(source, "mac-quotes-tnr")
for item in source.rglob("*"):
    relative = item.relative_to(source)
    if item.is_file() and snapshot._included(relative):
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(item, target)
relative = "src/PartyOps.DocumentFormatter.AddIn/DocumentRepository/Services/Formatting/EastAsianPunctuationFontService.cs"
revision = subprocess.check_output(["git", "rev-parse", "f889ed3"], cwd=root, text=True).strip()
content = subprocess.check_output(["git", "show", revision + ":vendor/document-formatter-source/" + relative], cwd=root)
(destination / relative).write_bytes(content)
# 聚合指纹严格使用现有 Windows 门禁，不能以重算哈希替代预期值。
evidence = snapshot.verify(destination, "upstream")
evidence.update({"historical_revision": revision, "restored_file": relative, "current_mac_source_preserved": True, "destination": str(destination)})
(Path(__file__).parent / "windows-formatter-source-recovery.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(evidence, ensure_ascii=False))

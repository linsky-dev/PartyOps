"""提取失败必须保留底层原因，同时保持哈希与安装事务的既有门禁。"""
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
INSTALLER = Path(os.environ.get("PARTYOPS_DIAGNOSTIC_ISS", str(
    ROOT / "packaging/windows/PartyOps.iss")))


def dotnet_function():
    text = INSTALLER.read_text(encoding="utf-8")
    return text.split("function EnsureDotNet48", 1)[1].split("function CanLaunchAfterInstall", 1)[0]


def test_extraction_failure_keeps_original_reason_before_return():
    body = dotnet_function()
    failure = body.split("  except", 1)[1].split("  end;", 1)[0]
    assert failure.index("ExtractError := GetExceptionMessage;") < failure.index(
        "Log('[DOTNET48_EXTRACT_FAILED] ' + ExtractError);")
    assert failure.index("Log(") < failure.index("Result := '[DOTNET48_EXTRACT_FAILED]")
    assert "exit;" in failure


def test_prerequisite_exec_stays_after_payload_hash_guard():
    body = dotnet_function()
    assert body.index("ExtractTemporaryFile(DotNet48InstallerName)") < body.index(
        "InstallerHash := GetSHA256OfFile(InstallerPath)")
    assert body.index("[DOTNET48_HASH_MISMATCH]") < body.index("if not Exec(")
    assert "CompareText(InstallerHash, DotNet48InstallerSha256) <> 0" in body
    assert "if not HasDotNet48 then" in body

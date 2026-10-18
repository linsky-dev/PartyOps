"""仅本地准备 Windows 替换；无网络写入口、不签名、不生成发布通过标记。"""
from __future__ import annotations
import argparse
import copy
import importlib.util
import json
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
PRIOR = Path("C:/PartyOps-VM-Lab/records/kylin-20261012/acceptance/publication-20261016")
RECOVERY = PRIOR.parent / "publication-recovery-20261016"
BASELINE = PRIOR / "batch/release-manifest-kylin-arm64-20261016.json"
BASELINE_SHA = "b52807452932502a5f05a10eb609c87345fabe4addb17d3ffeae3a673e0d0e57"
SHA = "3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27"
SIZE = 449978846
NAME = "PartyOps_1.4.5-rc.6_windows_amd64.exe"
PROPOSED_NAME = "PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe"
PACKAGE = ROOT / "artifacts" / NAME
BUILD = Path("D:/PartyOps-VM-Lab/state/builds") / (SHA + ".json")
sys.dont_write_bytecode = True
sys.path.insert(0, str(PRIOR))
sys.path.insert(0, str(ROOT / "qa/vm-lab"))
spec = importlib.util.spec_from_file_location("prior_publication_readonly_helpers", PRIOR / "publication.py")
prior = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prior)


def checked_local_path(path):
    """只允许当前 C 工作区、C 历史发布记录与 D 构建回执；拒绝 E 和网络路径。"""
    path = Path(path).resolve(strict=True)
    allowed = (ROOT.resolve(), PRIOR.parents[2].resolve(), Path("D:/PartyOps-VM-Lab").resolve(),
               Path("C:/PartyOps-VM-Lab/website-recovered-20261014/public/releases/v1.4.5-rc.6").resolve())
    prior.require(path.drive.upper() in {"C:", "D:"} and any(path.is_relative_to(p) for p in allowed), "LOCAL_C_D_INPUT_REQUIRED")
    return path


def evidence(path):
    path = checked_local_path(path)
    return {"path": str(path), "bytes": path.stat().st_size, "sha256": prior.digest(path)}


def windows_only_plan(manifest, metadata, candidate):
    """原九包及其 acceptance/signature 对象原值保留；只识别一个 Windows 替换。"""
    assets = manifest["assets"]
    packages = manifest["acceptance"]["packages"]
    prior.require(len(assets) == len(packages) == len(metadata["assets"]) == 10, "TEN_CURRENT_PACKAGES_REQUIRED")
    prior.require(len({a["filename"] for a in assets}) == 10, "DUPLICATE_BASELINE_ASSET")
    old = [a for a in assets if a["filename"] == NAME]
    old_acceptance = [p for p in packages if p["id"] == "windows_amd64"]
    prior.require(len(old) == len(old_acceptance) == 1 and old_acceptance[0]["filename"] == NAME, "ONE_WINDOWS_BASELINE_REQUIRED")
    expected = {(a["filename"], a["size"], a["sha256"]) for a in assets}
    actual = {(a["file_name"], a["bytes"], a["sha256"]) for a in metadata["assets"]}
    prior.require(expected == actual, "SIGNED_BASELINE_AND_CURRENT_RECEIVER_DIFFER")
    prior.require(candidate["sha256"] != old[0]["sha256"], "CANDIDATE_IS_OLD_WINDOWS")
    return {"scope": "windows_amd64-only-replacement-local-plan", "old_windows": copy.deepcopy(old[0]),
        "candidate": candidate, "retained_assets": copy.deepcopy([a for a in assets if a["filename"] != NAME]),
        "retained_acceptance": copy.deepcopy([p for p in packages if p["id"] != "windows_amd64"]),
        "historical_manifest_policy": "保留全部原签名清单原字节；本计划未签名，不能替换正式清单。",
        "publication_status": "blocked-pending-host-uac-and-wps-acceptance", "formal_release_ready": False,
        "network_write_allowed": False, "old_windows_withdrawal_allowed": False,
        "distribution_name_proposal": PROPOSED_NAME,
        "name_reason": "既有接收器禁止同名不同SHA覆盖；建议新文件名先追加、全量回读后再切入口，旧包保留回滚。"}


def preflight():
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
    from windows_build_payload import expected_payload
    from types import SimpleNamespace
    prior.require(ROOT.drive.upper() == "C:", "CURRENT_C_WORKSPACE_REQUIRED")
    package = evidence(PACKAGE)
    prior.require(package["bytes"] == SIZE and package["sha256"] == SHA, "ACTUAL_WINDOWS_PACKAGE_CHANGED")
    with PACKAGE.open("rb") as stream:
        prior.require(stream.read(2) == b"MZ", "WINDOWS_PE_HEADER_REQUIRED")
    record = prior.read(checked_local_path(BUILD))
    prior.require(record.get("status") == "completed" and not record.get("errors"), "COMPLETED_FORMAL_BUILD_REQUIRED")
    # 正式回执记录的源码身份用于本地载荷核对；不据此宣称当前全部源码门禁重新通过。
    bound, payload, _ = expected_payload(SimpleNamespace(root=Path("D:/PartyOps-VM-Lab")),
        {"id": "windows_amd64", "version": "1.4.5-rc.6", "path": str(PACKAGE), "sha256": SHA}, record["source_before"])
    prior.require(prior.digest(checked_local_path(BASELINE)) == BASELINE_SHA, "PUBLISHED_SIGNED_BASELINE_CHANGED")
    baseline = prior.read(BASELINE)
    key = Ed25519PublicKey.from_public_bytes(prior.trusted_key())
    prior.verify_signature(baseline, key)
    for asset in baseline["assets"]:
        prior.verify_signature(asset, key)
    metadata_path = PRIOR / "batch/post-switch-receiver/upload-metadata.json"
    metadata = prior.read(checked_local_path(metadata_path))
    prior.require(metadata["manifest_sha256"] == BASELINE_SHA, "CURRENT_RECEIVER_BASELINE_CHANGED")
    plan = windows_only_plan(baseline, metadata, {"filename": NAME, "bytes": SIZE, "sha256": SHA})
    qa = HERE.parent
    receipts = []
    for name in ("overlay-install/completed.json", "retention-after-overlay.json", "normal-reboot-completed.json", "retention-after-reboot.json"):
        path = qa / name
        value = prior.read(checked_local_path(path))
        prior.require(value.get("candidate", {}).get("package", {}).get("sha256") == SHA, "WIN10_RECEIPT_PACKAGE_MISMATCH")
        if name.startswith("retention-"):
            prior.require(value.get("status") == "passed", "WIN10_RETENTION_RECEIPT_NOT_PASSED")
        elif name.startswith("overlay-"):
            prior.require(value.get("status") == "completed" and value.get("install", {}).get("exit_code") == 0, "OVERLAY_NOT_COMPLETED")
        else:
            prior.require(value.get("restart", {}).get("status") == "passed" and value.get("restart", {}).get("actual") is True, "NORMAL_REBOOT_NOT_COMPLETED")
        receipts.append(evidence(path))
    history = []
    for name in ("release-manifest.json", "release-manifest-macos-arm64-20261009.json", "release-manifest-arm64-startup-diagnostic-20261010.json", "release-manifest-kylin-arm64-20261016.json"):
        history.append(evidence(Path("C:/PartyOps-VM-Lab/website-recovered-20261014/public/releases/v1.4.5-rc.6") / name))
    helpers = [evidence(PRIOR / name) for name in ("publication.py", "receiver_factory.py", "github_publication.py")]
    return {"local_input_status": "valid", "publication_status": plan["publication_status"], "network_write_allowed": False,
        "package": package, "formal_build_receipt": evidence(BUILD), "source_fingerprint_at_build": bound["source_fingerprint"],
        "embedded_manifest": payload["manifest"], "executable_bindings": payload["executables"],
        "published_baseline": evidence(BASELINE), "receiver_baseline": evidence(metadata_path),
        "win10_receipts": receipts, "historical_signed_manifests": history, "reused_helper_sources": helpers,
        "reusable_interfaces": {
            "domestic": {"source": str(PRIOR / "publication.py"), "entry": "release_client() -> cloudstudio-scoped-upload.upload_one(root,name,base_url,token_path,profile)",
                "boundary": "须为Windows独立参数化verify_release/file_health；国内完整GET回读后才能进入后续阶段。"},
            "receiver": {"source": str(PRIOR / "receiver_factory.py"), "entry": "build_receiver(retained,candidate,manifest_sha,require_candidate=False)",
                "boundary": "旧PROFILE/封存前缀/文案硬编码麒麟，不可直接重放；Windows新批次只允许一个新包上传，旧十包只读保留。"},
            "backend_deploy": {"source": str(RECOVERY / "workbuddy-cloudstudio-bridge.py"), "entry": "官方WorkBuddy connector-proxy / workbuddy_sites_deploy；先独立后台预检与一次新intent",
                "boundary": "旧桥只接受fixed-eight/nine/ten profile；不能伪称兼容新Windows批次，也不能套用已消耗intent或凭据。"},
            "github": {"source": str(PRIOR / "github_publication.py"), "entry": "gh_api / anonymous_hash / find_asset / 独占写intent后upload",
                "boundary": "只复用已有gh认证机制，不读取token；旧upload/documents/release-body入口固化麒麟，未来须Windows限域适配。"}},
        "replacement_plan": plan, "credentials_read": False, "deploy_called": False, "uploaded": False,
        "rollback": {"website_deployment": "dpgtwf7htv3j", "github_main_at_prior_publication": "b58cd0505ebd8bc187d40857e92d19c88b2a2b56",
            "old_windows": plan["old_windows"], "old_package_current_public_availability": "not-rechecked-this-run",
            "rule": "本轮不删除旧Windows包/附件，不改原九包及历史清单；网络切换前须再快照当前远端事实。"},
        "pending_inputs": ["真实主机安装/UAC失败与恢复验收回执", "真实WPS排版与金样验收回执",
            "主控确认发布范围、唯一分发文件名及Windows独立签名清单", "现有WorkBuddy后台预检、sandbox/域名的当前只读绑定",
            "GitHub当前release/tag/main及附件快照", "完整官网候选与预览检查，国内完整回读通过后才进入GitHub与官网切换"]}


def prepare():
    value = preflight()
    output = HERE / "prepared"
    output.mkdir(exist_ok=False)
    # 原包只创建本地硬链接，不复制450MB、不接触其它平台二进制。
    os.link(PACKAGE, output / NAME)
    prior.require(prior.digest(output / NAME) == SHA, "LOCAL_PACKAGE_LINK_CHANGED")
    with (output / (NAME + ".sha256")).open("x", encoding="utf-8", newline="\n") as stream:
        stream.write(SHA + "  " + NAME + "\n")
    value["local_sidecar"] = evidence(output / (NAME + ".sha256"))
    value["local_package_link"] = evidence(output / NAME)
    prior.write(output / "preparation.json", value)
    return {"local_preparation": "created", "publication_status": value["publication_status"], "network_write_allowed": False,
            "metadata": str(output / "preparation.json"), "retained_other_packages": 9}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["preflight", "prepare"])
    args = parser.parse_args()
    value = preflight() if args.mode == "preflight" else prepare()
    # 不打印业务验收正文、令牌、私钥或原包之外的内容。
    print(json.dumps({k: v for k, v in value.items() if k in {"local_input_status", "local_preparation", "publication_status", "network_write_allowed", "metadata", "retained_other_packages"}}, ensure_ascii=False))


if __name__ == "__main__":
    main()

"""隔离冻结 ARM15 候选应用源码；不创建提交、不上传源码、不生成发布结论。"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess

BASE = "b9b469fa5f146198c92db32ccce30996f672f12e"
ROOTS = ("backend", "frontend", "packaging", "scripts", "vendor", "assets", "config")
ROOT_FILES = (".gitattributes", ".gitignore", "LICENSE", "README.md", "THIRD_PARTY_NOTICES.md",
              "CHANGELOG.md", "SECURITY.md", "CONTRIBUTING.md", "DESIGN.md", "install.sh", "一键安装党建智办.sh")
PATCH_FILES = {"packaging/macos/build-pkg.sh", "packaging/macos/partyops.spec",
               "packaging/macos/validate-bundle.sh", "scripts/build-document-formatter-host-unix.sh",
               "scripts/validate-source-formatter-runtime.py", "scripts/macos-mono-input-profile.py",
               "packaging/macos/build-native-runtimes.sh", "backend/app/official_format_host.py",
               "backend/app/package_selftest.py"}
CS_FILES = {"packaging/windows/formatter-host/" + name + ".cs" for name in
            ("Program", "PortableWpsComBridge", "SourceOptionScope", "MacWpsObjectBridge",
             "MacTaskLeaseRuntime", "MacSessionCoordinator")}
MANAGED_BINDING_SHA = "edbf9f11a555e3e72e1e4f1b4d7282fb319f4a45f8dd15cfe6392ee787035af4"
REQUIRED = {"backend/pyproject.toml", "backend/uv.lock", "frontend/package.json",
            "frontend/pnpm-lock.yaml", "packaging/uos/entrypoint.py", "packaging/uos/client_entrypoint.py",
            "packaging/uos/wizard_entrypoint.py", "packaging/uos/update-public-key.txt",
            "packaging/windows/partyops-1024.png", "packaging/macos/launcher.py",
            "packaging/macos/launcher-wrapper.c", "packaging/macos/launch_agent_entrypoint.py",
            "packaging/macos/updater_entrypoint.py", "scripts/validate-portable-tar.py",
            "scripts/prepare-libreoffice-macos.sh", "packaging/macos/build-native-runtimes.sh"} | PATCH_FILES | CS_FILES

def digest(data):
    return hashlib.sha256(data).hexdigest()

def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, sort_keys=True, indent=2) + "\n", encoding="utf-8")

def safe_path(root, name):
    if not isinstance(name, str):
        raise ValueError("路径必须是字符串")
    relative = PurePosixPath(name)
    if not name or relative.is_absolute() or ".." in relative.parts or "\\" in name or ":" in name or relative.as_posix() != name:
        raise ValueError("非法相对路径")
    path = root / name
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError("路径越界")
    for ancestor in (path, *path.parents):
        if ancestor == root:
            break
        if ancestor.is_symlink():
            raise ValueError("输入不允许符号链接")
    return path

def regular_files(root):
    result = []
    for path in root.rglob("*"):
        if path.is_symlink():
            raise ValueError("目录含符号链接")
        if path.is_file():
            result.append(path.relative_to(root).as_posix())
        elif not path.is_dir():
            raise ValueError("目录含特殊文件")
    return sorted(result)

def row(root, name):
    path = safe_path(root, name)
    if not path.is_file():
        raise ValueError("冻结文件缺失：" + name)
    content = path.read_bytes()
    return {"path": name, "bytes": len(content), "sha256": digest(content)}

def validate_rows(root, rows):
    if not isinstance(rows, list) or not rows:
        raise ValueError("输入清单为空")
    names = set()
    for expected in rows:
        name = expected["path"]
        if name in names or row(root, name) != expected:
            raise ValueError("文件重复或摘要不符：" + name)
        names.add(name)
    return names

def load_bound(path, expected):
    if not re.fullmatch(r"[0-9a-f]{64}", expected):
        raise ValueError("外部 SHA256 无效")
    raw = path.read_bytes()
    if digest(raw) != expected:
        raise ValueError("外部 SHA256 不匹配：" + path.name)
    return json.loads(raw)

def git(root, args, accepted=(0,)):
    result = subprocess.run(["git", "-c", "core.quotePath=false", "-C", str(root), *args],
                            stdin=subprocess.DEVNULL, capture_output=True, timeout=120)
    if result.returncode not in accepted:
        raise ValueError("本地 Git 操作失败：" + args[0])
    return result.stdout

def tree(root, revision):
    result = {}
    for entry in git(root, ["ls-tree", "-r", "-z", revision, "--", *ROOTS, *ROOT_FILES]).split(b"\0"):
        if not entry:
            continue
        fields, name = entry.split(b"\t", 1)
        mode, kind, object_id = fields.decode().split()
        if mode not in ("100644", "100755") or kind != "blob":
            raise ValueError("应用源码 tree 包含链接或特殊项")
        result[name.decode("utf-8")] = (mode, object_id)
    if not result:
        raise ValueError("应用源码集合为空")
    return result

def approve(checkout, input_root, approval_path, approval_sha, candidate):
    approval = load_bound(safe_path(input_root, approval_path), approval_sha)
    if (approval.get("schema") != 1 or approval.get("kind") != "arm15-unsigned-candidate-input-approval"
            or approval.get("base_commit") != BASE
            or approval.get("candidate_commit", candidate) != candidate
            or not re.fullmatch(r"[0-9a-f]{40}", candidate)
            or git(checkout, ["rev-parse", "HEAD"]).decode().strip() != candidate):
        raise ValueError("审批未绑定固定基底与实际候选提交")
    approved = approval["approved_application_files"]
    if validate_rows(checkout, approved) != PATCH_FILES:
        raise ValueError("审批必须准确列明 ARM profile/包/OCR 与两份 Mac 后端共九文件")
    changed = set(git(checkout, ["diff", "--name-only", BASE, candidate, "--", *ROOTS, *ROOT_FILES]).decode().splitlines())
    if not changed.issubset(PATCH_FILES):
        raise ValueError("候选含未批准应用源码差异")
    patch = git(checkout, ["diff", "--binary", BASE, candidate, "--", *ROOTS, *ROOT_FILES])
    if digest(patch) != approval["application_patch_sha256"]:
        raise ValueError("实际候选应用补丁 SHA 不符")
    if git(checkout, ["status", "--porcelain", "--", *ROOTS, *ROOT_FILES]).strip():
        raise ValueError("候选 checkout 应用源码存在未提交差异")
    probe = approval["native_probe"]
    probe_data = load_bound(safe_path(input_root, probe["execution_receipt"]), probe["execution_sha256"])
    load_bound(safe_path(input_root, probe["structure_receipt"]), probe["structure_sha256"])
    if probe_data.get("passed") is not True or not probe_data.get("bundled_assemblies") or not probe_data.get("posix_loaded_image"):
        raise ValueError("原生静态模板未具备真实通过证据")
    managed_root = safe_path(input_root, "managed-input")
    manifest = load_bound(safe_path(managed_root, "input-binding.json"), MANAGED_BINDING_SHA)
    names = validate_rows(managed_root, manifest["managed_files"])
    if (manifest.get("schema") != 1 or manifest.get("kind") != "same-source-anycpu-build-input"
            or names != set(regular_files(managed_root)) - {"input-binding.json"} or len(names) != 21
            or sum(item["bytes"] for item in manifest["managed_files"]) != 7921537
            or manifest.get("compiler_exit_code") != 0):
        raise ValueError("托管载荷真实文件集合、数量或字节不匹配")
    if validate_rows(checkout, manifest["source"]) != CS_FILES:
        raise ValueError("托管载荷未绑定相同六个 C# 源码")
    return approval, managed_root, patch

def stage(args):
    checkout, inputs, bundle = args.checkout.resolve(), args.input_root.resolve(), args.bundle.resolve()
    approval, managed_root, patch = approve(checkout, inputs, args.approval, args.approval_sha256, args.candidate_commit)
    if bundle.exists() or bundle.is_relative_to(checkout) or bundle.is_relative_to(inputs):
        raise ValueError("隔离 bundle 必须为 checkout/输入树外的新目录")
    candidate_tree, base_tree = tree(checkout, args.candidate_commit), tree(checkout, BASE)
    if not REQUIRED.issubset(candidate_tree):
        raise ValueError("应用集合未覆盖 spec/build 必要源码：" + ",".join(sorted(REQUIRED - candidate_tree.keys())))
    bundle.mkdir(parents=True)
    source, baseline = bundle / "application-source", bundle / "base-application"
    source.mkdir()
    baseline.mkdir()
    for name, (mode, _) in candidate_tree.items():
        original = safe_path(checkout, name)
        destination = safe_path(source, name)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(original, destination)
        destination.chmod(0o755 if mode == "100755" else 0o644)
    for name, (mode, object_id) in base_tree.items():
        destination = safe_path(baseline, name)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(git(checkout, ["cat-file", "blob", object_id]))
        destination.chmod(0o755 if mode == "100755" else 0o644)
    shutil.copytree(managed_root, bundle / "runtime-inputs" / "managed", symlinks=False,
                    ignore=shutil.ignore_patterns("input-binding.json"))
    (bundle / "approved-application.patch").write_bytes(patch)
    write_json(bundle / "source-index.json", {"schema": 1, "base_commit": BASE,
        "candidate_commit": args.candidate_commit, "approval_sha256": args.approval_sha256,
        "application_patch_sha256": approval["application_patch_sha256"], "paths": sorted(candidate_tree),
        "source_prebuild": [row(source, name) for name in sorted(candidate_tree)]})
    write_json(bundle / "input-acceptance.json", {"schema": 1, "native_probe_run_id": approval["native_probe"]["run_id"],
        "native_probe_passed": True, "managed_file_count": 21,
        "managed_bytes": 7921537, "managed_input_binding_sha256": MANAGED_BINDING_SHA, "source_copy_only": True,
        "gui_wps_validated": False, "publication_authorized": False})
    print(json.dumps({"bundle": str(bundle), "source": str(source)}))

def client_paths(source):
    client = safe_path(source, "frontend/dist/client")
    names = regular_files(client)
    if "index.html" not in names:
        raise ValueError("预构建 client 缺失 index.html")
    return ["frontend/dist/client/" + name for name in names]

def freeze(args):
    bundle = args.bundle.resolve()
    index = json.loads((bundle / "source-index.json").read_text(encoding="utf-8"))
    source = bundle / "application-source"
    paths = set(index["paths"])
    original_client = {name for name in paths if name.startswith("frontend/dist/client/")}
    # 预构建只允许 client 变化；源码本身必须仍与批准提交逐字节一致。
    validate_rows(source, [item for item in index["source_prebuild"] if item["path"] not in original_client])
    paths -= original_client
    current_client = client_paths(source)
    paths.update(current_client)
    snapshot = bundle / "frozen-application"
    snapshot.mkdir()
    for name in sorted(paths):
        destination = safe_path(snapshot, name)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(safe_path(source, name), destination)
    # 从真实基底应用树到真实冻结树的完整二进制 diff，包含新增 profile 和 client。
    diff = git(bundle, ["diff", "--no-index", "--binary", "--src-prefix=a/", "--dst-prefix=b/",
                        "base-application", "frozen-application"], accepted=(0, 1))
    diff = diff.replace(b"a/base-application/", b"a/").replace(b"b/frozen-application/", b"b/")
    (bundle / "tracked-before.diff").write_bytes(diff)
    rows = [row(source, name) for name in sorted(paths)]
    manifest = {"schema": 1, "kind": "complete-application-source-inputs-candidate-pending",
        "head": index["candidate_commit"], "base_commit": index["base_commit"],
        "candidate_commit": index["candidate_commit"], "approval_sha256": index["approval_sha256"],
        "application_patch_sha256": index["application_patch_sha256"],
        "dirty_diff_sha256": digest(diff), "file_count": len(rows), "files": rows}
    write_json(bundle / "source-inputs.json", manifest)
    write_json(bundle / "freeze-receipt.json", {"schema": 1, "manifest_sha256": digest((bundle / "source-inputs.json").read_bytes()),
        "file_count": len(rows), "dirty_diff_sha256": digest(diff), "client_paths": current_client,
        "base_commit": index["base_commit"], "candidate_commit": index["candidate_commit"],
        "application_patch_sha256": index["application_patch_sha256"],
        "gui_wps_validated": False, "publication_authorized": False})
    print((bundle / "freeze-receipt.json").read_text(encoding="utf-8"))

def verify(args):
    bundle = args.bundle.resolve()
    source = bundle / "application-source"
    manifest = load_bound(bundle / "source-inputs.json", args.manifest_sha256)
    if (not re.fullmatch(r"[1-9][0-9]*", args.file_count) or manifest.get("file_count") != int(args.file_count)
            or len(manifest["files"]) != int(args.file_count)):
        raise ValueError("外部冻结数量绑定不符")
    validate_rows(source, manifest["files"])
    if digest((bundle / "tracked-before.diff").read_bytes()) != manifest["dirty_diff_sha256"]:
        raise ValueError("真实 frozen diff 已变化")
    frozen_client = sorted(item["path"] for item in manifest["files"] if item["path"].startswith("frontend/dist/client/"))
    if frozen_client != client_paths(source):
        raise ValueError("重复前端构建改变 client 文件集合")
    print("FROZEN_APPLICATION_SOURCE_VERIFIED")

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    preparation = commands.add_parser("stage")
    preparation.add_argument("--checkout", type=Path, required=True)
    preparation.add_argument("--input-root", type=Path, required=True)
    preparation.add_argument("--approval", required=True)
    preparation.add_argument("--approval-sha256", required=True)
    preparation.add_argument("--candidate-commit", required=True)
    preparation.add_argument("--bundle", type=Path, required=True)
    commands.add_parser("freeze").add_argument("--bundle", type=Path, required=True)
    validation = commands.add_parser("verify")
    validation.add_argument("--bundle", type=Path, required=True)
    validation.add_argument("--manifest-sha256", required=True)
    validation.add_argument("--file-count", required=True)
    args = parser.parse_args()
    {"stage": stage, "freeze": freeze, "verify": verify}[args.command](args)

if __name__ == "__main__":
    main()

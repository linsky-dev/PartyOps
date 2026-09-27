"""在 UUID 绑定的丢弃式 Guest 内安装真实 DEB/RPM 并采集安装后诊断。

此探针只覆盖安装和包自检，不冒充 GUI、公文、模型及完整生命周期通过。
"""
import argparse
import hashlib
import json
import os
import platform
import subprocess
from datetime import datetime, timedelta, timezone
from pathlib import Path


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--uuid", required=True)
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--arch", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    marker = json.loads(Path("/etc/partyops-vm-lab.json").read_text())
    if marker != {"uuid": args.uuid, "purpose": "disposable-qa"}:
        raise RuntimeError("GUEST_OWNERSHIP_MISMATCH")
    if os.getuid() == 0:
        raise RuntimeError("PROBE_REQUIRES_STANDARD_USER")
    actual_arch = {"loong64": "loongarch64"}.get(platform.machine(), platform.machine())
    if actual_arch != args.arch or digest(args.package) != args.sha256:
        raise RuntimeError("PACKAGE_HASH_OR_ISA_MISMATCH")
    args.output.mkdir(parents=True, exist_ok=True)
    result = {"generated_at": datetime.now(timezone(timedelta(hours=8))).isoformat(),
              "guest_uuid": args.uuid, "package_sha256": args.sha256, "arch": actual_arch,
              "boot_id": Path("/proc/sys/kernel/random/boot_id").read_text().strip(),
              "uid": os.getuid(), "steps": [], "runtime_environment_passed": False,
              "status": "partial", "scope": "installed-package-diagnostic-only"}

    def step(name, command, timeout=900, environment=None):
        log = args.output / (name + ".log")
        try:
            with log.open("w") as output:
                completed = subprocess.run(command, stdout=output, stderr=subprocess.STDOUT,
                                           timeout=timeout, check=False, env=environment)
            ok = completed.returncode == 0
            entry = {"id": name, "status": "passed" if ok else "failed", "exit_code": completed.returncode}
        except subprocess.TimeoutExpired:
            ok = False
            entry = {"id": name, "status": "failed", "error": "TIMEOUT"}
        entry.update(evidence=log.name, evidence_sha256=digest(log))
        result["steps"].append(entry)
        if not ok:
            result["status"] = "failed"
        (args.output / "installed-probe.json").write_text(json.dumps(result, indent=2))
        return ok

    suffix = args.package.suffix
    if suffix == ".rpm":
        metadata = ["rpm", "-qp", "--qf", "%{NAME} %{VERSION}-%{RELEASE} %{ARCH}\\n", str(args.package)]
        # 同一候选版本修复后须覆盖已安装载荷；仍由前置哈希和 Guest 身份检查绑定实际包。
        install = ["sudo", "-n", "rpm", "-Uvh", "--replacepkgs", str(args.package)]
        installed = ["rpm", "-q", "--qf", "%{NAME} %{VERSION}-%{RELEASE} %{ARCH}\\n", "partyops"]
    elif suffix == ".deb":
        metadata = ["dpkg-deb", "-f", str(args.package), "Package", "Version", "Architecture"]
        install = ["sudo", "-n", "dpkg", "-i", str(args.package)]
        installed = ["dpkg-query", "-W", "-f=${Package} ${Version} ${Architecture} ${Status}\\n", "partyops"]
    else:
        raise RuntimeError("UNSUPPORTED_PACKAGE")
    if not step("package-metadata", metadata, 60):
        return 2
    installed_ok = step("actual-install", install)
    step("installed-metadata", installed, 60)
    if installed_ok:
        step("package-selftest-standard-user", ["/opt/partyops/partyops", "--package-self-test"])
        step("wizard-layout-standard-user", ["/opt/partyops/partyops-wizard", "--runtime-layout-self-test"])
        wizard_root = args.output / "wizard-selftest"
        wizard_root.mkdir(exist_ok=True)
        step("wizard-desktop-server", ["/opt/partyops/partyops-wizard", "--desktop-server-self-test"],
             environment={**os.environ, "PARTYOPS_DESKTOP_SELFTEST_ROOT": str(wizard_root.resolve())})
    step("install-transaction-status", ["sudo", "-n", "systemctl", "show", "partyops-install-verify.service", "--no-pager"], 60)
    step("install-journal", ["sudo", "-n", "journalctl", "-u", "partyops-install-verify.service", "-n", "100", "--no-pager"], 60)
    # 任何结果都不是完整门禁通过；由主控结合其余生命周期证据再评估。
    return 0 if result["status"] == "partial" else 2


if __name__ == "__main__":
    raise SystemExit(main())

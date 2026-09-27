"""macOS Intel 验收环境的可信介质校验与 OC4VM/VMware 引导。"""
from __future__ import annotations

import hashlib
import json
import os
import shutil
import struct
import subprocess
import zipfile
from pathlib import Path

from evidence import checked_id, now, safe_child, sha256, write_json
from providers import run

OC4VM_VERSION = "3.0.1"
OC4VM_COMMIT = "33fad1b4a6083b8b8fef93f395f2e384743967ac"
OC4VM_ARCHIVE_SHA256 = "143b947e6d538a8b635a9c919e611705b03356964a7cfcbbe96e923e54dc1dbb"
RECOVERYOS_VERSION = "1.0.1"
RECOVERYOS_COMMIT = "0012c39adde4c557196eea556edffc63951e7ff6"
RECOVERYOS_ARCHIVE_SHA256 = "63925566a90bb410971df56b38943c410f630e799c70402be747163edca4f1ce"
MACOS_RECOVERY_VERSION = "15.7.4"
MACOS_RECOVERY_BOARD_ID = "Mac-937A206F2EE63C01"
MACOS_RECOVERY_MLB = "00000000000000000"

# Apple EFI ROM 公钥。用于独立验证 Apple 下载返回的 chunklist 签名。
APPLE_EFI_ROM_PUBLIC_KEY = int(
    "C3E748CAD9CD384329E10E25A91E43E1A762FF529ADE578C935BDDF9B13F2179D4855E6FC89E9E29CA12517D17DFA1EDCE0BEBF0EA7B461FFE61D94E2BDF72C196F89ACD3536B644064014DAE25A15DB6BB0852ECBD120916318D1CCDEA3C84C92ED743FC176D0BACA920D3FCF3158AFF731F88CE0623182A8ED67E650515F75745909F07D415F55FC15A35654D118C55A462D37A3ACDA08612F3F3F6571761EFCCBCC299AEE99B3A4FD6212CCFFF5EF37A2C334E871191F7E1C31960E010A54E86FA3F62E6D6905E1CD57732410A3EB0C6B4DEFDABE9F59BF1618758C751CD56CEF851D1C0EAA1C558E37AC108DA9089863D20E2E7E4BF475EC66FE6B3EFDCF",
    16,
)


def verify_apple_recovery(dmg_path: Path, chunklist_path: Path) -> dict:
    """校验 Apple chunklist 的签名，并逐块校验恢复 DMG。"""
    data = chunklist_path.read_bytes()
    header_format = "<4sIBBBBQQQ"
    header_size = struct.calcsize(header_format)
    if len(data) < header_size:
        raise RuntimeError("APPLE_CHUNKLIST_TRUNCATED")
    magic, declared_header, version, chunk_method, signature_method, _reserved, count, chunk_offset, signature_offset = struct.unpack_from(
        header_format, data
    )
    if magic != b"CNKL" or declared_header < header_size or chunk_offset < declared_header:
        raise RuntimeError("APPLE_CHUNKLIST_INVALID_HEADER")
    descriptor_size = 36
    descriptor_end = chunk_offset + count * descriptor_size
    if descriptor_end > signature_offset or signature_offset > len(data):
        raise RuntimeError("APPLE_CHUNKLIST_INVALID_OFFSETS")
    signed_digest = hashlib.sha256(data[:signature_offset]).digest()
    signature = data[signature_offset:]
    if signature_method == 1:
        if len(signature) != 256:
            raise RuntimeError("APPLE_CHUNKLIST_INVALID_SIGNATURE_SIZE")
        signature_value = int.from_bytes(signature[::-1], "big")
        plaintext = pow(signature_value, 0x10001, APPLE_EFI_ROM_PUBLIC_KEY)
        expected = int(
            "1" + "f" * 404 + "003031300d060960864801650304020105000420" + signed_digest.hex(),
            16,
        )
        if plaintext != expected:
            raise RuntimeError("APPLE_CHUNKLIST_SIGNATURE_INVALID")
    elif signature_method == 2:
        if signature != signed_digest:
            raise RuntimeError("APPLE_CHUNKLIST_DIGEST_INVALID")
    else:
        raise RuntimeError("APPLE_CHUNKLIST_SIGNATURE_METHOD_UNSUPPORTED")

    total = 0
    with dmg_path.open("rb") as image:
        for index in range(count):
            offset = chunk_offset + index * descriptor_size
            size = struct.unpack_from("<I", data, offset)[0]
            expected_hash = data[offset + 4:offset + descriptor_size]
            payload = image.read(size)
            if len(payload) != size:
                raise RuntimeError(f"APPLE_RECOVERY_CHUNK_TRUNCATED:{index}")
            if hashlib.sha256(payload).digest() != expected_hash:
                raise RuntimeError(f"APPLE_RECOVERY_CHUNK_HASH_MISMATCH:{index}")
            total += size
        if image.read(1):
            raise RuntimeError("APPLE_RECOVERY_IMAGE_LARGER_THAN_CHUNKLIST")
    return {
        "header_version": version,
        "chunk_method": chunk_method,
        "signature_method": signature_method,
        "chunk_count": count,
        "verified_bytes": total,
        "dmg_sha256": sha256(dmg_path),
        "chunklist_sha256": sha256(chunklist_path),
    }


class VMwareMacLab:
    """只管理实验室登记的 macOS Intel VM，不修改 VMware 安装文件。"""

    def __init__(self, qemu_lab, active_root: Path | None = None):
        self.lab = qemu_lab
        self.root = qemu_lab.root
        configured = Path(qemu_lab.defaults["fallback_root"])
        self.active_root = (active_root or configured).resolve()
        self.oc4vm_archive = self.root / "downloads/oc4vm-3.0.1/oc4vm-3.0.1.zip"
        self.oc4vm_root = self.root / "tools/oc4vm-3.0.1"
        self.recovery_archive = self.root / "downloads/recoveryOS-1.0.1/recoveryOS-1.0.1.zip"
        self.recovery_tool = self.root / "tools/recoveryOS-1.0.1/windows/amd64/macrecovery.exe"
        self.recovery_dir = self.root / "downloads/apple-macos-15.7.4-recovery"

    def vmware_tool(self, name: str) -> Path:
        suffix = ".exe" if os.name == "nt" else ""
        candidates = [self.root / "tools/VMware" / f"{name}{suffix}"]
        if name == "vmware-vmx":
            candidates.insert(0, self.root / "tools/VMware/x64" / f"{name}{suffix}")
        discovered = shutil.which(name)
        if discovered:
            candidates.append(Path(discovered))
        for candidate in candidates:
            if candidate.is_file():
                return candidate.resolve()
        raise RuntimeError(f"MISSING_VMWARE_TOOL:{name}")

    @property
    def state_path(self) -> Path:
        return self.root / "state/macos-x64-vm.json"

    def state(self) -> dict:
        if not self.state_path.is_file():
            raise RuntimeError("MACOS_VM_NOT_BOOTSTRAPPED")
        return json.loads(self.state_path.read_text(encoding="utf-8"))

    def save(self, state: dict) -> dict:
        write_json(self.state_path, state)
        return state

    def running_vms(self) -> list[Path]:
        output = run([str(self.vmware_tool("vmrun")), "-T", "ws", "list"], timeout=30)
        lines = [line.strip() for line in output.splitlines()[1:] if line.strip()]
        return [Path(line).resolve() for line in lines]

    def running(self, state: dict | None = None) -> bool:
        current = state or self.state()
        expected = os.path.normcase(str(Path(current["vmx"]).resolve()))
        return any(os.path.normcase(str(path)) == expected for path in self.running_vms())

    def start(self, gui: bool = False) -> dict:
        state = self.state()
        if self.running(state):
            state.update(status="running", observed_at=now())
            return self.save(state)
        roots = {self.root, self.active_root}
        for path in [p for root in roots for p in (root / "vms").glob("*/vm.json")]:
            other = json.loads(path.read_text(encoding="utf-8"))
            if self.lab.live(other) or self.lab.process_active(other):
                raise RuntimeError(f"CONCURRENCY_LIMIT:{other.get('target', path.parent.name)}")
        running = self.running_vms()
        if running:
            raise RuntimeError(f"CONCURRENCY_LIMIT:{running[0]}")
        self.lab.check_space(Path(state["vmx"]).parent, 2 if state.get("snapshots") else 20)
        if os.name == "nt":
            import psutil
            needed = (8192 + self.lab.defaults["host_free_memory_gib"] * 1024) * 1024**2
            if psutil.virtual_memory().available < needed:
                raise RuntimeError("HOST_MEMORY_HEADROOM")
        run([str(self.vmware_tool("vmrun")), "-T", "ws", "start", state["vmx"], "gui" if gui else "nogui"], timeout=90,
            log=self.root / "state/macos-x64-start.log")
        state.update(status="running", started_at=now(), start_mode="gui" if gui else "nogui")
        return self.save(state)

    def stop(self, force: bool = False) -> dict:
        state = self.state()
        if self.running(state):
            run([str(self.vmware_tool("vmrun")), "-T", "ws", "stop", state["vmx"], "hard" if force else "soft"], timeout=180,
                log=self.root / "state/macos-x64-stop.log")
            state.update(status="stopped", stopped_at=now(), stop_mode="hard" if force else "soft")
        else:
            state.update(status="stopped", observed_at=now())
        return self.save(state)

    def snapshot(self, name: str, restore: bool = False) -> dict:
        checked_id(name)
        state = self.state()
        if self.running(state):
            raise RuntimeError("MACOS_SNAPSHOT_REQUIRES_STOPPED_VM")
        snapshots = state.setdefault("snapshots", [])
        if restore and name not in snapshots:
            raise RuntimeError("MACOS_SNAPSHOT_NOT_FOUND")
        if not restore and name in snapshots:
            return state
        operation = "revertToSnapshot" if restore else "snapshot"
        run([str(self.vmware_tool("vmrun")), "-T", "ws", operation, state["vmx"], name], timeout=600,
            log=self.root / "state" / f"macos-x64-{operation}-{name}.log")
        if not restore:
            snapshots.append(name)
        state.update(status="snapshot_restored" if restore else "snapshot_created", snapshot_at=now())
        return self.save(state)

    def _verified_archive(self, path: Path, expected: str, label: str) -> None:
        if not path.is_file():
            raise RuntimeError(f"MISSING_{label}_ARCHIVE")
        if sha256(path) != expected:
            raise RuntimeError(f"{label}_ARCHIVE_HASH_MISMATCH")

    def _verify_extracted_file(self, archive: Path, suffix: str, extracted: Path) -> str:
        if not extracted.is_file():
            raise RuntimeError(f"MISSING_EXTRACTED_FILE:{extracted.name}")
        with zipfile.ZipFile(archive) as bundle:
            matches = [entry for entry in bundle.infolist() if entry.filename.replace("\\", "/").endswith(suffix)]
            if len(matches) != 1:
                raise RuntimeError(f"ARCHIVE_ENTRY_AMBIGUOUS:{suffix}")
            digest = hashlib.sha256(bundle.read(matches[0])).hexdigest()
        if digest != sha256(extracted):
            raise RuntimeError(f"EXTRACTED_FILE_HASH_MISMATCH:{extracted.name}")
        return digest

    def template_manifest(self) -> dict:
        self._verified_archive(self.oc4vm_archive, OC4VM_ARCHIVE_SHA256, "OC4VM")
        template = self.oc4vm_root / "vmware/intel"
        hashes = {}
        for name in ("macos.nvram", "macos.plist", "macos.vmdk", "macos.vmx", "opencore.iso", "opencore.vmdk"):
            hashes[name] = self._verify_extracted_file(
                self.oc4vm_archive, f"vmware/intel/{name}", template / name
            )
        return {"version": OC4VM_VERSION, "commit": OC4VM_COMMIT, "archive_sha256": OC4VM_ARCHIVE_SHA256, "files": hashes}

    def recovery_paths(self) -> tuple[Path, Path, Path]:
        base = self.recovery_dir / "macos-15.7.4-recovery"
        return Path(str(base) + ".dmg"), Path(str(base) + ".chunklist"), Path(str(base) + ".vmdk")

    def download_recovery(self) -> dict:
        self._verified_archive(self.recovery_archive, RECOVERYOS_ARCHIVE_SHA256, "RECOVERYOS")
        self._verify_extracted_file(self.recovery_archive, "windows/amd64/macrecovery.exe", self.recovery_tool)
        self.recovery_dir.mkdir(parents=True, exist_ok=True)
        dmg, chunklist, _vmdk = self.recovery_paths()
        if not dmg.is_file() or not chunklist.is_file():
            log = self.root / "state/macos-x64-recovery-download.log"
            command = [
                str(self.recovery_tool), "-action", "download", "-board-id", MACOS_RECOVERY_BOARD_ID,
                "-mlb", MACOS_RECOVERY_MLB, "-os-type", "default", "-outdir", str(self.recovery_dir),
                "-basename", "macos-15.7.4-recovery",
            ]
            log.parent.mkdir(parents=True, exist_ok=True)
            with log.open("wb") as output:
                completed = subprocess.run(command, stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT,
                                           timeout=1800, check=False)
            if completed.returncode:
                raise RuntimeError(f"APPLE_RECOVERY_DOWNLOAD_FAILED:{completed.returncode}")
        verification = verify_apple_recovery(dmg, chunklist)
        result = {
            "verified_at": now(), "source": "Apple Internet Recovery", "requested_version": MACOS_RECOVERY_VERSION,
            "board_id": MACOS_RECOVERY_BOARD_ID, "mlb_kind": "anonymous-zero",
            "recoveryos_version": RECOVERYOS_VERSION, "recoveryos_commit": RECOVERYOS_COMMIT,
            "recoveryos_archive_sha256": RECOVERYOS_ARCHIVE_SHA256, **verification,
        }
        write_json(self.root / "state/macos-x64-recovery-media.json", result)
        return result

    def prepare_recovery(self) -> dict:
        result = self.download_recovery()
        dmg, _chunklist, vmdk = self.recovery_paths()
        if not vmdk.is_file():
            temporary = vmdk.with_suffix(".vmdk.partial")
            run([self.lab.binary("qemu-img"), "convert", "-f", "dmg", "-O", "vmdk", str(dmg), str(temporary)], timeout=900,
                log=self.root / "state/macos-x64-recovery-convert.log")
            run([self.lab.binary("qemu-img"), "check", str(temporary)], timeout=300)
            temporary.replace(vmdk)
        info = json.loads(run([self.lab.binary("qemu-img"), "info", "--output=json", str(vmdk)]))
        if info.get("format") != "vmdk" or info.get("backing-filename"):
            raise RuntimeError("APPLE_RECOVERY_VMDK_INVALID")
        result.update(vmdk=str(vmdk), vmdk_sha256=sha256(vmdk), vmdk_info=info)
        write_json(self.root / "state/macos-x64-recovery-media.json", result)
        return result

    def doctor(self) -> dict:
        findings = {
            "generated_at": now(), "runtime_environment_passed": False,
            "vmware": {}, "cpu_features": {}, "oc4vm": {}, "recovery": {}, "guest": {},
        }
        install_receipt_path = self.root / "state/vmware-workstation-17.6.4-install.json"
        install_receipt = json.loads(install_receipt_path.read_text(encoding="utf-8")) if install_receipt_path.is_file() else {}
        for name in ("vmrun", "vmware", "vmware-vmx"):
            try:
                tool = self.vmware_tool(name)
                # 三个入口的版本均取自已核验的安装收据；直接传 -v 可能启动图形/VMX 进程。
                item = {"path": str(tool), "sha256": sha256(tool),
                        "version": install_receipt.get("version"), "build": install_receipt.get("build")}
                findings["vmware"][name] = item
            except RuntimeError as exc:
                findings["vmware"][name] = {"error": str(exc)}
        cpuid = self.oc4vm_root / "tools/windows/cpuid.exe"
        try:
            output = run([str(cpuid)], timeout=30, log=self.root / "state/macos-x64-cpuid.log")
            labels = {"avx": "AVX instructions", "avx2": "Advanced Vector Extensions 2.0 (AVX2)",
                      "f16c": "16-bit FP conversion instructions", "rdrand": "RDRAND instruction"}
            findings["cpu_features"] = {name: label in output for name, label in labels.items()}
            findings["cpu_features"]["hypervisor_detected"] = "Hyper-V detected" in output
        except (OSError, RuntimeError) as exc:
            findings["cpu_features"] = {"error": str(exc)}
        try:
            findings["oc4vm"] = self.template_manifest()
        except RuntimeError as exc:
            findings["oc4vm"] = {"error": str(exc)}
        dmg, chunklist, vmdk = self.recovery_paths()
        findings["recovery"] = {"dmg": dmg.is_file(), "chunklist": chunklist.is_file(), "vmdk": vmdk.is_file()}
        state = self.state_path
        if state.is_file():
            findings["guest"] = json.loads(state.read_text(encoding="utf-8"))
        findings["ready_to_bootstrap"] = (
            all(findings["cpu_features"].get(name) is True for name in ("avx", "avx2", "f16c", "rdrand"))
            and "error" not in findings["oc4vm"] and dmg.is_file() and chunklist.is_file() and vmdk.is_file()
            and all("error" not in item for item in findings["vmware"].values())
        )
        write_json(self.root / "state/macos-doctor.json", findings)
        return findings

    def bootstrap(self) -> dict:
        media = self.prepare_recovery()
        template_manifest = self.template_manifest()
        self.active_root.mkdir(parents=True, exist_ok=True)
        marker = self.active_root / ".partyops-vm-lab.json"
        if not marker.exists():
            write_json(marker, {"purpose": "PartyOps active VM storage", "created_at": now()})
        usage = shutil.disk_usage(self.active_root)
        if usage.free < 60 * 1024**3:
            raise RuntimeError("MACOS_VM_DISK_HEADROOM: requires 60 GiB before bootstrap")
        vm_dir = safe_child(self.active_root, self.active_root / "vms/macos-x64")
        state_path = self.state_path
        if state_path.is_file():
            return json.loads(state_path.read_text(encoding="utf-8"))
        if vm_dir.exists():
            raise RuntimeError("INCOMPLETE_MACOS_VM_CREATE: existing directory preserved")
        vm_dir.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(self.oc4vm_root / "vmware/intel", vm_dir)
        _dmg, _chunklist, recovery_vmdk = self.recovery_paths()
        shutil.copyfile(recovery_vmdk, vm_dir / "recovery.vmdk")
        vmx = vm_dir / "macos.vmx"
        text = vmx.read_text(encoding="utf-8")
        text = text.replace('displayName = "macOS INTEL"', 'displayName = "PartyOps macOS 15 Intel Acceptance"')
        text = text.replace('sata0:1.autodetect = "TRUE"', 'sata0:1.autodetect = "FALSE"')
        text = text.replace('sata0:1.deviceType = "cdrom-raw"', 'sata0:1.deviceType = "disk"')
        text = text.replace('sata0:1.fileName = "auto detect"', 'sata0:1.fileName = "recovery.vmdk"')
        text = text.replace('sata0:1.startConnected = "FALSE"', 'sata0:1.startConnected = "TRUE"')
        vmx.write_text(text, encoding="utf-8", newline="\n")
        state = {
            "schema_version": 1, "target": "macos-x64", "status": "bootstrapped", "temporary": True,
            "created_at": now(), "environment_type": "hardware-virtualized", "vm_dir": str(vm_dir),
            "vmx": str(vmx), "vmx_sha256": sha256(vmx), "oc4vm": template_manifest,
            "recovery": media, "vmware_patched": False, "unlocker_used": False,
        }
        write_json(state_path, state)
        return state

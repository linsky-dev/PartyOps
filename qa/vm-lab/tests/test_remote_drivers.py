"""SSH 编排用内存替身验证，模拟结果只写 pytest 临时目录。"""
import importlib.util
import io
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

HERE = Path(__file__).resolve().parents[1]


def module(filename, name):
    spec = importlib.util.spec_from_file_location(name, HERE / "scripts" / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


class MemoryFile(io.StringIO):
    def __exit__(self, *args):
        return False


class FakeSftp:
    def __init__(self, identity="owned-uuid"):
        self.identity = identity
        self.written = {}
    def __enter__(self):
        return self
    def __exit__(self, *args):
        return False
    def file(self, path, mode="r"):
        if mode == "r":
            return io.StringIO(json.dumps({"uuid": self.identity, "purpose": "disposable-qa"}))
        output = MemoryFile()
        self.written[path] = output
        return output
    def mkdir(self, path):
        pass
    def put(self, source, destination):
        assert Path(source).exists()
    def listdir(self, path):
        return ["installed-probe.json"]
    def get(self, source, destination):
        Path(destination).write_text('{"status":"partial"}')


class FakeStream(io.BytesIO):
    channel = SimpleNamespace(recv_exit_status=lambda: 0)


class FakeClient:
    def __init__(self, identity="owned-uuid"):
        self.identity, self.closed, self.commands = identity, False, []
        self.sftp = FakeSftp(identity)
    def load_host_keys(self, path):
        pass
    def load_system_host_keys(self):
        pass
    def set_missing_host_key_policy(self, policy):
        pass
    def connect(self, *args, **kwargs):
        pass
    def exec_command(self, command, **kwargs):
        self.commands.append(command)
        output = self.identity if command == "cat /sys/class/dmi/id/product_uuid" else ""
        return None, FakeStream(output.encode()), FakeStream()
    def open_sftp(self):
        return self.sftp
    def close(self):
        self.closed = True


@pytest.fixture
def remote(tmp_path, monkeypatch):
    (tmp_path / "keys").mkdir()
    (tmp_path / "keys/guest_ed25519.pub").write_text("ssh-ed25519 UNIT_TEST_ONLY")
    state = {"uuid": "owned-uuid"}
    matrix = {"defaults": {"primary_root": str(tmp_path)}, "targets": {"openeuler-iso-x64": {"os": "linux", "arch": "x86_64", "ssh_port": 22211}}, "packages": {"rpm_x86_64": {"required_targets": ["openeuler-iso-x64"]}}}
    def ssh(target, command):
        if "cat /etc/" in command:
            raise RuntimeError("not provisioned yet")
        return "1000"
    lab = SimpleNamespace(root=tmp_path, matrix=matrix, state=lambda _: state, live=lambda _: True, ssh=ssh)
    client = FakeClient()
    def configure(driver):
        monkeypatch.setattr(driver, "load_configuration", lambda: (matrix, {}))
        monkeypatch.setattr(driver, "QemuLab", lambda *a: lab)
        if hasattr(driver, "paramiko"):
            monkeypatch.setattr(driver.paramiko, "SSHClient", lambda: client)
    return lab, client, configure


def test_provision_verifies_guest_and_locks_public_initial_password(remote):
    lab, client, configure = remote
    driver = module("provision-openeuler.py", "provision_test")
    configure(driver)
    result = driver.provision("openeuler-iso-x64")
    assert result["root_default_password_locked"]
    assert any("passwd -l root" in command for command in client.commands)
    assert client.closed
    assert (lab.root / "state/provision-openeuler-iso-x64.json").exists()


def test_provision_idempotent_does_not_retry_locked_password(remote):
    lab, client, configure = remote
    driver = module("provision-openeuler.py", "provision_idempotent")
    configure(driver)
    lab.ssh = lambda *a: '{"uuid":"owned-uuid","purpose":"disposable-qa"}'
    assert driver.provision("openeuler-iso-x64")["status"] == "already_provisioned"
    assert not client.commands


@pytest.mark.parametrize("reason", ["not_running", "uuid", "standard_user"])
def test_provision_fail_closed(remote, reason):
    lab, client, configure = remote
    driver = module("provision-openeuler.py", "provision_refuse")
    configure(driver)
    if reason == "not_running":
        lab.live = lambda _: False
    elif reason == "uuid":
        client.identity = "other-vm"
    else:
        def ssh(*args):
            if "cat /etc/" in args[1]:
                raise RuntimeError("not provisioned")
            return "0"
        lab.ssh = ssh
    with pytest.raises(RuntimeError):
        driver.provision("openeuler-iso-x64")
    assert not any("passwd -l root" in command for command in client.commands)


def test_install_driver_collects_partial_not_full_acceptance(remote, monkeypatch):
    lab, client, configure = remote
    driver = module("exercise-linux-install.py", "exercise_test")
    configure(driver)
    package = lab.root / "package.rpm"
    package.write_bytes(b"fixture")
    monkeypatch.setattr(driver, "inventory", lambda *a: ({"rpm_x86_64": {"path": str(package), "sha256": driver.sha256(package)}}, {}))
    result = driver.exercise("openeuler-iso-x64")
    assert result["runtime_environment_passed"] is False
    assert Path(result["report_path"], "installed-probe.json").exists()
    assert client.closed


def test_install_driver_rejects_unrelated_guest(remote, monkeypatch):
    lab, client, configure = remote
    driver = module("exercise-linux-install.py", "exercise_refuse")
    configure(driver)
    package = lab.root / "package.rpm"
    package.write_bytes(b"fixture")
    monkeypatch.setattr(driver, "inventory", lambda *a: ({"rpm_x86_64": {"path": str(package), "sha256": driver.sha256(package)}}, {}))
    client.sftp.identity = "not-owned"
    with pytest.raises(RuntimeError, match="GUEST_UUID"):
        driver.exercise("openeuler-iso-x64")
    lab.live = lambda _: False
    with pytest.raises(RuntimeError, match="OWNED_LINUX"):
        driver.exercise("openeuler-iso-x64")


def test_real_reboot_requires_changed_kernel_boot_id(remote, monkeypatch):
    lab, _client, configure = remote
    driver = module("reboot-linux.py", "reboot_test")
    configure(driver)
    ids = iter(["boot-before", "boot-before", "boot-after"])
    def ssh(target, command, **kwargs):
        if "cat /etc/" in command:
            return '{"uuid":"owned-uuid"}'
        if "systemctl reboot" in command:
            raise RuntimeError("SSH closed during reboot")
        return next(ids)
    lab.ssh = ssh
    monkeypatch.setattr(driver.time, "sleep", lambda _: None)
    result = driver.reboot("openeuler-iso-x64")
    assert result["actual"] is True
    assert result["boot_id_before"] != result["boot_id_after"]


def test_reboot_timeout_cannot_be_passed(remote, monkeypatch):
    lab, _client, configure = remote
    driver = module("reboot-linux.py", "reboot_timeout")
    configure(driver)
    lab.ssh = lambda target, command, **kw: '{"uuid":"owned-uuid"}' if "cat /etc/" in command else "same-boot"
    ticks = iter([0, 601])
    monkeypatch.setattr(driver.time, "monotonic", lambda: next(ticks))
    with pytest.raises(RuntimeError, match="GUEST_REBOOT_TIMEOUT"):
        driver.reboot("openeuler-iso-x64")


def test_reboot_rejects_unrelated_machine(remote):
    lab, _client, configure = remote
    driver = module("reboot-linux.py", "reboot_invalid")
    configure(driver)
    lab.ssh = lambda *a: '{"uuid":"not-owned"}'
    with pytest.raises(RuntimeError, match="UUID_MISMATCH"):
        driver.reboot("openeuler-iso-x64")
    lab.live = lambda _: False
    with pytest.raises(RuntimeError, match="OWNED_RUNNING"):
        driver.reboot("openeuler-iso-x64")


@pytest.mark.parametrize("shutdown_ok", [True, False])
def test_whpx_restarts_only_after_verified_normal_shutdown(remote, monkeypatch, shutdown_ok):
    lab, _client, configure = remote
    driver = module("reboot-linux.py", "reboot_cold_test")
    configure(driver)
    target = "openeuler-iso-x64"
    lab.matrix["targets"][target]["whpx_legacy_irq"] = True
    lab.state(target).update(acceleration="whpx", qmp_port=12345)
    events, boots = [], iter(["boot-before", "boot-after"])
    lab.ssh = lambda _target, command, **kw: '{"uuid":"owned-uuid"}' if "cat /etc/" in command else next(boots)
    def observe(*args):
        events.append("observe")
        if not shutdown_ok:
            raise RuntimeError("QEMU_EXIT_WITHOUT_GUEST_SHUTDOWN_EVENT")
        return {"event": "SHUTDOWN", "data": {"guest": True, "reason": "guest-shutdown"}}
    monkeypatch.setattr(driver, "poweroff_event", observe)
    monkeypatch.setattr(driver, "wait_stopped", lambda *args: events.append("stopped"))
    lab.start = lambda *args: events.append("start")
    if shutdown_ok:
        result = driver.reboot(target)
        assert events == ["observe", "stopped", "start"]
        assert result["actual"] and result["normal_shutdown_observed"]
        assert result["boot_id_before"] != result["boot_id_after"]
    else:
        with pytest.raises(RuntimeError, match="WITHOUT_GUEST_SHUTDOWN"):
            driver.reboot(target)
        assert events == ["observe"]
        record = json.loads((lab.root / "reports" / target / "reboot-boot-before.json").read_text())
        assert record["status"] == "failed" and record["actual"] is False


def test_guest_input_script_keeps_error_codes_and_quotes_paths():
    driver = module("prepare-linux-runtime-inputs.py", "runtime_input_test")
    script = driver.verification_script("/home/qa/中文 WORK '路径", {"ocr": {"filename": "x.png", "sha256": "abc"}})
    compile(script, "guest-input-validation", "exec")
    assert "STANDARD_USER_WORK_REQUIRED" in script
    assert "INPUT_HASH_MISMATCH" in script
    assert "MODEL_IMPORT_GUEST_SPACE_REQUIRED" in script


def test_ocr_only_runtime_inputs_do_not_depend_on_model_files(tmp_path):
    driver = module("prepare-linux-runtime-inputs.py", "runtime_input_ocr_only_test")
    assert driver.argument_parser().parse_args(["loongson-core"]).ocr_only is False
    assert driver.argument_parser().parse_args(["loongson-core", "--ocr-only"]).ocr_only is True
    # OCR 模式的 target 配置故意不含 arch，确保不会借 target 名称推断模型能力。
    files = driver.runtime_input_files("loongson-core", {"targets": {"loongson-core": {}}}, tmp_path,
                                       ocr_only=True)
    assert set(files) == {"ocr"}
    image, checksum = files["ocr"]
    assert image == tmp_path / "reports/uos-deb-x64/ocr-input-20260906.png"
    assert checksum == "070bd312f8654446bcca77c4efaea6df05e6a6ffba631da71e542bacb112d8d6"

    script = driver.verification_script("/home/qa", {"ocr": {"filename": image.name, "sha256": checksum}},
                                        required_free_bytes=16 * 1024**2,
                                        space_error_code="RUNTIME_INPUT_GUEST_SPACE_REQUIRED")
    compile(script, "ocr-only-input-validation", "exec")
    assert str(16 * 1024**2) in script
    assert "RUNTIME_INPUT_GUEST_SPACE_REQUIRED" in script
    assert "MODEL_IMPORT_GUEST_SPACE_REQUIRED" not in script


def test_windows_probe_uses_encoded_powershell_and_rejects_wrong_identity(tmp_path, monkeypatch):
    driver = module("probe-windows.py", "windows_probe_test")
    root = tmp_path / "lab"
    (root / "keys").mkdir(parents=True)
    (root / "keys/guest_ed25519").write_text("fixture")
    vm = root / "vms/win11-x64"
    vm.mkdir(parents=True)
    state = {"uuid": "owned-windows-uuid"}
    matrix = {
        "defaults": {"primary_root": str(root), "qemu_home": str(root / "qemu")},
        "targets": {"win11-x64": {"os": "windows", "os_release": "11", "arch": "x86_64", "ssh_port": 22111}},
    }
    lab = SimpleNamespace(
        root=root,
        state=lambda _target: state,
        live=lambda _state: True,
        vm_dir=lambda _target: vm,
    )
    payload = {
        "os": "windows", "os_release": "11", "arch": "x86_64",
        "boot_id": "boot-one", "distribution": "Windows",
        "timezone": "China Standard Time",
        "identity": {"uuid": "owned-windows-uuid", "purpose": "disposable-qa"},
    }
    client = FakeClient()
    monkeypatch.setattr(driver, "load_configuration", lambda: (matrix, {}))
    monkeypatch.setattr(driver, "QemuLab", lambda *args: lab)
    monkeypatch.setattr(driver.paramiko, "SSHClient", lambda: client)
    monkeypatch.setattr(driver, "execute", lambda _client, script, timeout=90: json.dumps(payload))
    result = driver.probe("win11-x64")
    assert result["owned_guest"] is True
    assert json.loads((vm / "guest-system.json").read_text())["boot_id"] == "boot-one"
    assert "-EncodedCommand " in driver.encoded_powershell("Write-Output ok")

    payload["identity"]["uuid"] = "other-windows-vm"
    with pytest.raises(RuntimeError, match="UUID_MISMATCH"):
        driver.probe("win11-x64")


def test_windows_install_driver_keeps_diagnostic_partial(tmp_path, monkeypatch):
    driver = module("exercise-windows-install.py", "windows_install_test")
    root = tmp_path / "lab"
    (root / "keys").mkdir(parents=True)
    package = tmp_path / "PartyOps_1.4.5-rc.6_windows_amd64.exe"
    package.write_bytes(b"windows package fixture")
    package_hash = driver.sha256(package)
    state = {"uuid": "owned-windows-uuid"}
    matrix = {
        "version": "1.4.5-rc.6",
        "defaults": {"primary_root": str(root)},
        "required_cases": ["clean_install", "restart_persistence"],
        "targets": {"win11-x64": {"os": "windows", "arch": "x86_64", "ssh_port": 22111}},
        "packages": {"windows_amd64": {"required_targets": ["win11-x64"]}},
    }
    lab = SimpleNamespace(root=root, matrix=matrix, state=lambda _: state, live=lambda _: True)
    client = FakeClient()
    client.sftp.get = lambda source, destination: Path(destination).write_text("installer completed")
    monkeypatch.setattr(driver, "load_configuration", lambda: (matrix, {}))
    monkeypatch.setattr(driver, "QemuLab", lambda *args: lab)
    probes = iter([{"installed_package": False}, {"installed_package": True}])
    monkeypatch.setattr(driver, "probe", lambda *args: next(probes))
    monkeypatch.setattr(driver, "runtime_binding", lambda *args: {"vm_uuid": state["uuid"], "baseline_id": "clean"})
    monkeypatch.setattr(driver, "fingerprint", lambda: "current-source")
    monkeypatch.setattr(driver, "bind_package", lambda _lab, package, source: {**package, "source_fingerprint": source})
    monkeypatch.setattr(driver, "inventory", lambda *args: ({"windows_amd64": {
        "id": "windows_amd64", "version": "1.4.5-rc.6", "path": str(package),
        "sha256": package_hash, "bytes": package.stat().st_size,
    }}, {}))
    monkeypatch.setattr(driver, "connect", lambda *args: client)
    def remote(_client, script, timeout=600):
        if "WindowsPrincipal" in script:
            return json.dumps({"uuid": state["uuid"], "purpose": "disposable-qa", "administrator": True})
        if "Get-FileHash" in script:
            return package_hash
        if "Start-Process" in script:
            return json.dumps({"exit_code": 0, "app_exists": True, "launcher_exists": True,
                               "formatter_exists": True, "uninstaller_exists": True,
                               "install_log_exists": True, "services": []})
        return ""
    monkeypatch.setattr(driver, "execute", remote)
    result = driver.exercise("win11-x64", tmp_path)
    assert result["status"] == "partial"
    assert result["runtime_environment_passed"] is False
    report = next((root / "reports/win11-x64").glob("*/installed-probe.json"))
    assert json.loads(report.read_text())["remaining_required_cases"] == matrix["required_cases"]

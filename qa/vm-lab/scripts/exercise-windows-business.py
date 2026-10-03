"""在 Windows 普通用户桌面启动安装版，复用现有 HTTP 业务验收断言。

HTTP 连接经 SSH 到同一 Guest 的回环端口；凭据只存 Guest 的私有目录。
不加载产品源码、不以管理员后台进程冒充普通用户桌面，不生成完整通过凭证。
"""
from __future__ import annotations

import argparse
import base64
import http.client
import http.cookiejar
import importlib.util
import json
import ntpath
import re
import secrets
import sys
import time
import urllib.parse
import urllib.request
import uuid
from pathlib import Path
from types import SimpleNamespace

import paramiko

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, sha256, write_json
from identity import probe, runtime_binding
from lab import REPO, fingerprint, inventory, load_configuration
from provenance import bind_package
from providers import QemuLab, qmp


def module(filename, name):
    spec = importlib.util.spec_from_file_location(name, filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


business = module(HERE / "guest/linux-business-lifecycle.py", "shared_installed_business")
windows = module(HERE / "scripts/exercise-windows-install.py", "windows_installer")
restart = module(HERE / "scripts/reboot-windows.py", "windows_restart")
require = business.require

def validate_test_data_path(remote, data_dir):
    require(re.fullmatch(r"lifecycle-[a-f0-9]{12}", remote) is not None, "UNREGISTERED_LIFECYCLE_DIRECTORY")
    expected = ntpath.join(r"C:\Users\partyopsuser\Documents\PartyOps QA", remote, "中文 空格业务数据")
    require(ntpath.normcase(data_dir) == ntpath.normcase(expected), "TEST_DATA_CLEANUP_OUTSIDE_OWNED_WORK")
    return expected


def verify_uninstalled(result):
    require(result.get("exit_code") == 0, "WINDOWS_UNINSTALLER_FAILED")
    require(result.get("registered") is False, "UNINSTALL_REGISTRATION_REMAINS")
    require(result.get("program_exists") is False, "UNINSTALL_LEFT_EXECUTABLE")
    require(result.get("services") == [], "UNINSTALL_SERVICES_REMAIN")
    require(result.get("processes") == [], "UNINSTALL_LEFT_INSTALLED_PROGRAM_RUNNING")


GUEST_OWNERSHIP = r"""
$ErrorActionPreference='Stop'
$marker=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
$hardware=(Get-CimInstance Win32_ComputerSystemProduct).UUID
if($hardware -ne $request.uuid -or $marker.uuid -ne $request.uuid -or $marker.purpose -ne 'disposable-qa') {throw 'GUEST_OWNERSHIP_MISMATCH'}
$data=[IO.Path]::GetFullPath($request.data_dir)
$expected='C:\Users\partyopsuser\Documents\PartyOps QA\'+$request.remote+'\中文 空格业务数据'
if($request.remote -notmatch '^lifecycle-[a-f0-9]{12}$' -or $data -ne $expected) {throw 'TEST_DATA_CLEANUP_OUTSIDE_OWNED_WORK'}
function Assert-NoReparse([string]$path) {
  $current=$path
  while($current) {
    if(Test-Path -LiteralPath $current) {
      if((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'TEST_DATA_REPARSE_REJECTED'}
    }
    $current=Split-Path -Path $current -Parent
  }
}
function Get-OwnedDataEntries([string]$root) {
  $pending=New-Object 'Collections.Generic.Queue[string]'
  $pending.Enqueue($root)
  while($pending.Count -gt 0) {
    foreach($entry in @(Get-ChildItem -LiteralPath $pending.Dequeue() -Force)) {
      if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'TEST_DATA_REPARSE_REJECTED'}
      $entry
      if($entry.PSIsContainer) {$pending.Enqueue($entry.FullName)}
    }
  }
}
Assert-NoReparse $data
$install='C:\PartyOps QA\中文 程序'
Assert-NoReparse $install
"""


class TunnelFile:
    def __init__(self, owner, stream):
        self.owner, self.stream, self.closed = owner, stream, False

    def __getattr__(self, name):
        return getattr(self.stream, name)

    def close(self):
        if not self.closed:
            self.closed = True
            self.stream.close()
            self.owner.files -= 1
            self.owner.finish_close()


class TunnelSocket:
    """模拟 socket.makefile 的引用寿命，HTTP/1.0 响应体读完前不能关闭 Channel。"""
    def __init__(self, channel):
        self.channel, self.files, self.closing = channel, 0, False

    def __getattr__(self, name):
        return getattr(self.channel, name)

    def makefile(self, *args, **kwargs):
        stream = self.channel.makefile(*args, **kwargs)
        self.files += 1
        return TunnelFile(self, stream)

    def close(self):
        self.closing = True
        self.finish_close()

    def finish_close(self):
        if self.closing and not self.files:
            self.channel.close()


class GuestHTTPConnection(http.client.HTTPConnection):
    def __init__(self, host, *, transport, **kwargs):
        super().__init__(host, **kwargs)
        self.transport = transport

    def connect(self):
        require(self.host == "127.0.0.1", "ONLY_GUEST_LOOPBACK_HTTP_ALLOWED")
        self.sock = TunnelSocket(self.transport.open_channel("direct-tcpip", (self.host, self.port),
                                                            ("127.0.0.1", 0), timeout=30))
        self.sock.settimeout(self.timeout)


class GuestHTTPHandler(urllib.request.HTTPHandler):
    def __init__(self, transport):
        super().__init__()
        self.transport = transport

    def http_open(self, request):
        return self.do_open(lambda host, **kwargs: GuestHTTPConnection(host, transport=self.transport, **kwargs), request)


class WindowsRun(business.GuestRun):
    def __init__(self, client, lab, target, phase, package, context, remote, reports):
        self.client, self.lab, self.target = client, lab, target
        self.package, self.context, self.remote = package, context, remote
        self.args = SimpleNamespace(phase=phase, uuid=context["environment"]["vm_uuid"], package_sha256=package["sha256"])
        self.reports = reports
        self.state_path = "/C:/ProgramData/PartyOps-VM-Lab/private/" + remote + ".json"
        self.state = {}
        if phase != "configure":
            with client.open_sftp() as fs, fs.file(self.state_path) as stream:
                self.state = json.load(stream)
            require(self.state["context"] == context, "LIFECYCLE_CONTEXT_CHANGED")
        self.boot = probe(lab, target)["boot_id"]
        self.report = {"scope": "windows-standard-desktop-and-installed-business", "phase": phase,
                       "generated_at": now(), "guest_uuid": self.args.uuid, "boot_id": self.boot,
                       "package_sha256": package["sha256"], "status": "failed",
                       "runtime_environment_passed": False, "checks": []}
        self.cookies = http.cookiejar.CookieJar()
        self.opener = self.make_opener(self.cookies)
        self.origin = "http://127.0.0.1:18775"

    def make_opener(self, cookies):
        return urllib.request.build_opener(urllib.request.ProxyHandler({}),
            GuestHTTPHandler(self.client.get_transport()), urllib.request.HTTPCookieProcessor(cookies))

    def persist(self):
        with self.client.open_sftp() as fs, fs.file(self.state_path, "w") as stream:
            stream.write(json.dumps(self.state, ensure_ascii=False).encode("utf-8"))

    def desktop(self, action):
        script = "& ([scriptblock]::Create([IO.File]::ReadAllText('C:\\ProgramData\\PartyOps-VM-Lab\\windows-standard-user.ps1',[Text.Encoding]::UTF8)))"
        return json.loads(windows.execute(self.client, script + " -ExpectedUuid " + self.args.uuid + " -Action " + action, timeout=120))

    def screenshot(self, label):
        state = self.lab.state(self.target)
        path = self.reports / (label + ".png")
        require(self.lab.live(state), "OWNED_GUEST_NOT_RUNNING")
        qmp(state["qmp_port"], "screendump", {"filename": str(path), "format": "png"})

    def desktop_start(self):
        info = self.desktop("Inspect")
        if any(p["name"] == "PartyOps.exe" for p in info["processes"]):
            self.require_configuration_owner(info)
            self.checked("standard-user-existing-runtime", {"desktop": info, "health": self.wait_health()})
            return info
        result = self.desktop("Launch")
        require(result["administrator"] is False and len(result["desktop"]) == 1, "STANDARD_DESKTOP_REQUIRED")
        self.checked("standard-user-interactive-launch", result)
        return result

    def after_reboot(self):
        require(self.state.get("business_verified"), "BUSINESS_PRECONDITION_MISSING")
        receipt = json.loads((self.lab.root / "state" / ("reboot-" + self.target + ".json")).read_text(encoding="utf-8"))
        restart.validate_restart(receipt["receipt"], self.context, self.lab.state(self.target))
        self.desktop_start()
        self.checked("guest-reboot-data-persistence", {"reboot_receipt": receipt["path"],
            "boot_id_before": receipt["receipt"]["identity_before"]["boot_id"], "boot_id_after": self.boot,
            "health": self.wait_health(), "preserved": self.check_business_data()})
        self.state["boot_id_after"] = self.boot
        self.persist()

    def wait_health(self):
        deadline = time.monotonic() + 300
        while True:
            try:
                result = self.request("/api/v1/health")
                require(result["status"] == "ok" and result["app_version"] == self.package["version"], "HEALTH_VERSION_MISMATCH")
                return result
            except (OSError, http.client.HTTPException, paramiko.SSHException):
                require(time.monotonic() < deadline, "INSTALLED_START_TIMEOUT")
                time.sleep(2)

    def configure(self):
        info = self.desktop("Inspect")
        require(info["config_dir"], "STANDARD_USER_PROFILE_MISSING")
        config = info["config_dir"].replace("\\", "/")
        with self.client.open_sftp() as fs:
            try:
                fs.stat("/" + config + "/mode.json")
            except FileNotFoundError:
                pass
            else:
                raise RuntimeError("FIRST_CONFIGURATION_REQUIRES_CLEAN_USER")
        self.state = {"context": self.context, "uuid": self.args.uuid, "package_sha256": self.package["sha256"],
                      "username": "lifecycleqa", "password": secrets.token_urlsafe(32),
                      "data_dir": "C:\\Users\\partyopsuser\\Documents\\PartyOps QA\\" + self.remote + "\\中文 空格业务数据",
                      "boot_id_before": self.boot}
        self.persist()
        # 续接已打开的同一普通用户向导，避免控制器重试时再启动一个 Launcher。
        if not info.get("wizard_url") or not any(p["name"] == "PartyOpsWizard.exe" for p in info["processes"]):
            self.desktop_start()
        self.finish_configuration()

    def resume_configure(self):
        info = self.desktop("Inspect")
        self.require_configuration_owner(info)
        require(info.get("wizard_url"), "ORIGINAL_WIZARD_NOT_RUNNING")
        with self.client.open_sftp() as fs:
            config = info["config_dir"].replace("\\", "/")
            with fs.file("/" + config + "/mode.json") as stream:
                mode = json.load(stream)
            require(mode["mode"] == "personal", "PARTIAL_CONFIGURATION_MODE_CHANGED")
            require(ntpath.normcase(mode["config_path"]) == ntpath.normcase(info["config_dir"] + "\\personal.env"),
                    "PARTIAL_CONFIGURATION_PATH_CHANGED")
            with fs.file("/" + mode["config_path"].replace("\\", "/")) as stream:
                values = stream.read().decode("utf-8-sig")
        data = re.search(r"^PARTYOPS_DATA_DIR=(.*)$", values, re.MULTILINE)
        require(data is not None and ntpath.normcase(data[1].strip().strip("'\"")) == ntpath.normcase(self.state["data_dir"]),
                "PARTIAL_CONFIGURATION_DATA_CHANGED")
        require(self.request("/api/v1/bootstrap/status")["configured"] is False, "RESUME_REQUIRES_UNCONFIGURED_ADMIN")
        self.checked("resume-same-partial-configuration", {"data_dir": self.state["data_dir"],
                                                           "health": self.wait_health(), "desktop": info})
        self.finish_configuration()

    @staticmethod
    def require_configuration_owner(info):
        require(info["administrator"] is False and len(info["desktop"]) == 1, "STANDARD_DESKTOP_REQUIRED")
        require(all(p["owner"] == info["username"] and p["session_id"] == info["desktop"][0]["session_id"]
                    for p in info["processes"] if p["name"] in {"PartyOpsWizard.exe", "PartyOps.exe"}),
                "CONFIGURATION_PROCESS_OWNER_CHANGED")

    def finish_configuration(self):
        deadline = time.monotonic() + 180
        while True:
            info = self.desktop("Inspect")
            self.require_configuration_owner(info)
            if info.get("wizard_url"):
                break
            require(time.monotonic() < deadline, "DESKTOP_WIZARD_MARKER_TIMEOUT")
            time.sleep(2)
        url = info["wizard_url"].strip()
        parsed = urllib.parse.urlparse(url)
        require(parsed.hostname == "127.0.0.1" and parsed.scheme == "http", "WIZARD_NOT_LOOPBACK")
        html = self.request("/", base=url).decode("utf-8")
        csrf = re.search(r'name="csrf" value="([^"]+)"', html)
        require(csrf is not None, "WIZARD_FORM_MISSING")
        self.checked("standard-user-desktop-wizard", {"url": url, "page_title": re.findall(r"<title>(.*?)</title>", html), "desktop": info})
        self.screenshot("wizard")

        def form(values):
            values["csrf"] = csrf[1]
            return self.request("/", "POST", urllib.parse.urlencode(values).encode(), base=url,
                                content_type="application/x-www-form-urlencoded")

        page = form({"mode": "personal", "port": "18775", "data_dir": self.state["data_dir"]}).decode("utf-8")
        require('name="mode" value="bootstrap_admin"' in page, "ADMIN_SETUP_FORM_MISSING")
        self.checked("personal-configuration", {"data_dir": self.state["data_dir"], "health": self.wait_health()})
        form({"mode": "bootstrap_admin", "username": self.state["username"], "display_name": "全生命周期 验收员", "password": self.state["password"]})
        user = self.login()
        self.state["user_id"] = user["id"]
        self.persist()
        require(self.request("/api/v1/bootstrap/status")["configured"] is True, "BOOTSTRAP_NOT_CONFIGURED")
        self.checked("first-admin-created-through-wizard", {"user_id": user["id"], "username": user["username"]})
        self.screenshot("configured")

    def upload_input(self, endpoint, path, checksum):
        require(path.is_file() and sha256(path) == checksum, "PINNED_RUNTIME_INPUT_MISSING_OR_CHANGED")
        boundary = "PartyOpsQA" + secrets.token_hex(12)
        header = (f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{path.name}"\r\n'
                  'Content-Type: application/octet-stream\r\n\r\n').encode()
        trailer = f'\r\n--{boundary}--\r\n'.encode()

        def chunks():
            # 大模型直接流过已验证的 SSH 通道，不在宿主内存复制多份大文件。
            yield header
            with path.open("rb") as stream:
                yield from iter(lambda: stream.read(65536), b"")
            yield trailer

        return self.request(endpoint, "POST", chunks(),
                            content_type="multipart/form-data; boundary=" + boundary,
                            extra_headers={"Content-Length": str(len(header) + path.stat().st_size + len(trailer))})

    def lifecycle_command(self, script, **values):
        data = validate_test_data_path(self.remote, self.state["data_dir"])
        payload = {"uuid": self.args.uuid, "remote": self.remote, "data_dir": data, **values}
        encoded = base64.b64encode(json.dumps(payload, ensure_ascii=False).encode("utf-8")).decode("ascii")
        prefix = "$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "'))|ConvertFrom-Json\n"
        return json.loads(windows.execute(self.client, prefix + GUEST_OWNERSHIP + script, timeout=3600))

    def inspect_owned_data(self):
        return self.lifecycle_command(r"""
if(-not(Test-Path -LiteralPath $data -PathType Container)) {throw 'BUSINESS_DATA_MISSING'}
$entries=@(Get-OwnedDataEntries $data)
$attachments=@($entries|Where-Object {-not $_.PSIsContainer -and $_.Length -lt 1MB}|ForEach-Object {
  $hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  if($hash -eq $request.attachment_sha256) {[ordered]@{path=$_.FullName;sha256=$hash}}
})
$config='C:\Users\partyopsuser\AppData\Local\PartyOps'
Assert-NoReparse $config
$settings=@('mode.json','personal.env')|ForEach-Object {
  $path=Join-Path $config $_
  if(Test-Path -LiteralPath $path) {[ordered]@{name=$_;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}}
}
[ordered]@{data_dir=$data;database_exists=(Test-Path -LiteralPath (Join-Path $data 'partyops.db'));attachments=@($attachments);settings=@($settings)}|ConvertTo-Json -Depth 6 -Compress
""", attachment_sha256=self.state["attachment_sha256"])

    def uninstall_keep(self):
        require(self.state.get("business_verified"), "BUSINESS_PRECONDITION_MISSING")
        self.checked("before-uninstall-business", self.check_business_data())
        before = self.inspect_owned_data()
        require(before["database_exists"] is True and bool(before["attachments"]), "BUSINESS_FILES_MISSING")
        require(len(before["settings"]) == 2, "PERSONAL_CONFIGURATION_MISSING")
        result = self.lifecycle_command(r"""
$uninstaller=Join-Path $install 'unins000.exe'
if(-not(Test-Path -LiteralPath $uninstaller -PathType Leaf)) {throw 'ACTUAL_UNINSTALLER_MISSING'}
$keys=@('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A}_is1')
$registration=@($keys|Where-Object {Test-Path -LiteralPath $_}|ForEach-Object {Get-ItemProperty -LiteralPath $_})
if($registration.Count -ne 1 -or $registration[0].InstallLocation.TrimEnd('\') -ne $install -or $registration[0].DisplayVersion -ne $request.version) {throw 'INSTALLED_REGISTRATION_BINDING_MISMATCH'}
$log='C:\PartyOps-QA\'+$request.remote+'-'+$request.phase+'-uninstall.log'
$arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DATAACTION=preserve /LOG="'+$log+'"'
$process=Start-Process -FilePath $uninstaller -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
$deadline=(Get-Date).AddSeconds(30)
do {
  $remaining=@(Get-CimInstance Win32_Process|Where-Object {$_.ExecutablePath -and $_.ExecutablePath.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase)}|Select-Object Name,ProcessId,ExecutablePath)
  if($remaining.Count -eq 0) {break}
  Start-Sleep -Milliseconds 500
}while((Get-Date) -lt $deadline)
$services=@(Get-Service -Name 'PartyOpsHost','PartyOpsUpdateService' -ErrorAction SilentlyContinue|ForEach-Object {$_.Name})
[ordered]@{exit_code=$process.ExitCode;registered=(@($keys|Where-Object {Test-Path -LiteralPath $_}).Count -gt 0);program_exists=(Test-Path -LiteralPath (Join-Path $install 'PartyOps.exe'));services=@($services);processes=@($remaining);log=$log;data_action='preserve'}|ConvertTo-Json -Depth 6 -Compress
""", version=self.package["version"], phase=self.args.phase)
        write_json(self.reports / "uninstall-result.json", result)
        with self.client.open_sftp() as fs:
            fs.get("/" + result["log"].replace("\\", "/"), str(self.reports / "uninstall.log"))
        verify_uninstalled(result)
        require(probe(self.lab, self.target).get("installed_package") is False, "INSTALLED_PACKAGE_STILL_DETECTED")
        after = self.inspect_owned_data()
        require(after == before, "UNINSTALL_CHANGED_BUSINESS_FILES_OR_SETTINGS")
        self.state["uninstall_preserved"] = {"package_sha256": self.package["sha256"], "data": after,
                                              "report_path": str(self.reports), "completed_at": now()}
        self.persist()
        self.checked("uninstall-program-removed-data-preserved", {"uninstall": result, "data": after,
                                                                 "account_and_task_recheck": "after-reinstall"})

    def same_package_reinstall(self):
        previous = self.state.get("uninstall_preserved", {})
        require(previous.get("package_sha256") == self.package["sha256"], "SAME_PACKAGE_REINSTALL_BINDING_MISSING")
        require(self.inspect_owned_data() == previous["data"], "REINSTALL_PRESERVED_DATA_CHANGED")
        report = windows.exercise(self.target, REPO / "artifacts", lab=self.lab)
        require(report["package_sha256"] == self.package["sha256"], "REINSTALL_PACKAGE_HASH_CHANGED")
        self.state["same_package_reinstalled"] = {"package_sha256": self.package["sha256"], "install": report}
        self.persist()
        self.checked("same-package-actually-reinstalled", {"install": report, "upgrade_baseline_test": False})
        self.after_reinstall()

    def after_reinstall(self):
        require(self.state.get("same_package_reinstalled", {}).get("package_sha256") == self.package["sha256"],
                "ACTUAL_SAME_PACKAGE_REINSTALL_REQUIRED")
        super().after_reinstall()
        self.state["reinstall_business_verified"] = True
        self.persist()

    def uninstall_remove_test_data(self):
        require(self.state.get("reinstall_business_verified") is True, "REINSTALL_BUSINESS_PRECONDITION_MISSING")
        self.uninstall_keep()
        result = self.lifecycle_command(r"""
if(-not(Test-Path -LiteralPath $data -PathType Container)) {throw 'OWNED_TEST_DATA_MISSING'}
$entries=@(Get-OwnedDataEntries $data)
$markerPath=Join-Path $data '.partyops-data-root.json'
$dataMarker=Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
$scopes=if($dataMarker.format_version -eq 1) {@($dataMarker.scope)} else {@($dataMarker.scopes)}
if($dataMarker.format_version -notin @(1,2) -or $dataMarker.product -ne 'PartyOps' -or $dataMarker.app_id -ne '1C8EFC63-CAFC-46EF-A5E3-D3D119B5BB3A' -or 'personal' -notin $scopes) {throw 'OWNED_TEST_DATA_MARKER_MISMATCH'}
# 精确解析后的目标仅为本轮 Guest 合成业务目录，不递归处理配置、账户或实验室证据。
Remove-Item -LiteralPath $data -Force -Recurse
[ordered]@{removed_data_dir=$data;data_absent=(-not(Test-Path -LiteralPath $data));private_evidence_preserved=(Test-Path -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\private');method='actual-uninstall-preserve-then-owned-test-directory-removal';native_all_users_delete_test=$false}|ConvertTo-Json -Compress
""")
        require(result["data_absent"] is True and result["private_evidence_preserved"] is True,
                "TEST_BUSINESS_DATA_REMOVAL_INCOMPLETE")
        self.state["test_data_removed"] = result
        self.persist()
        self.checked("uninstall-owned-test-data-removed", result)

    def ocr(self):
        self.wait_health()
        self.login()
        checksum = "070bd312f8654446bcca77c4efaea6df05e6a6ffba631da71e542bacb112d8d6"
        result = self.upload_input("/api/v1/intake/parse", self.lab.root / "reports/uos-deb-x64/ocr-input-20260906.png", checksum)
        write_json(self.reports / "ocr-response.json", {"input_sha256": checksum, "result": result})
        require(all(word in result["extracted_text"] for word in ("支部", "党员大会", "会议记录")), "CHINESE_OCR_TEXT_MISMATCH")
        require(result["warnings"] == [], "OCR_RETURNED_WARNING")
        self.checked("actual-image-ocr", {"input_sha256": checksum, "result": result})

    @staticmethod
    def require_models_available(status):
        # 一次回答后因内存不足暂停，只能保留该次推理证据，不能完成模型阶段。
        require(status.get("embedding_loaded") is True and status.get("embedding_available") is True,
                "EMBEDDING_UNAVAILABLE_AFTER_INFERENCE")
        require(status.get("llm_running") is True and status.get("llm_available") is True,
                "LOCAL_LLM_UNAVAILABLE_AFTER_INFERENCE")

    def models(self):
        require(self.state.get("business_verified"), "BUSINESS_PRECONDITION_MISSING")
        self.wait_health()
        self.login()
        inputs = {
            "embedding": ("PartyOps_BGE_Small_ZH_1.5.0.partyops-modelpack", "57df3669e368497ad6d428dab309fb7665229a57edfe3a4fe1f2afd6a0f1a155"),
            "llm": ("PartyOps_Qwen3_0.6B_Q8_0.partyops-modelpack", "deb85abe5c9d9c79fddb943424144770ed35499a4f5b9e15047812297f448d63"),
        }
        for capability, (filename, checksum) in inputs.items():
            existing = self.request("/api/v1/admin/ai/model-packs")
            pack = next((row for row in existing if row.get("sha256") == checksum), None)
            if pack is None:
                pack = self.upload_input("/api/v1/admin/ai/model-packs", REPO / "artifacts/model-packs" / filename, checksum)
            require(pack["status"] in {"installed", "active"} and pack["signature_valid"] is True, "SIGNED_MODEL_NOT_INSTALLED")
            if capability not in pack.get("active_capabilities", []):
                pack = self.request("/api/v1/admin/ai/model-packs/" + pack["id"] + "/activate?capability=" + capability, "POST")
            require(capability in pack["active_capabilities"], "SIGNED_MODEL_NOT_ACTIVE")
            self.checked("signed-model-" + capability, {"input_sha256": checksum, "pack_id": pack["id"], "active_capabilities": pack["active_capabilities"]})
            if capability == "embedding":
                task = self.task("原版系统 模型语义检索 " + secrets.token_hex(4))
                self.checked("semantic-index-business-input", {"task_id": task["id"]})
                deadline = time.monotonic() + 180
                while True:
                    result = self.request("/api/v1/global-search?q=" + urllib.parse.quote("原版系统"))
                    status = self.request("/api/v1/ai/runtime/status")
                    if status.get("embedding_loaded"):
                        break
                    require(time.monotonic() < deadline, "EMBEDDING_NOT_LOADED")
                    time.sleep(2)
                require(len(result.get("items", [])) >= 2, "SEMANTIC_RERANK_REQUIRES_MULTIPLE_RESULTS")
                self.checked("actual-semantic-search", {"result": result, "status": status})
            else:
                policies = self.request("/api/v1/ai/policies")
                if not any(row["active"] and "summarize" in row["capabilities"] for row in policies):
                    policy = self.request("/api/v1/ai/policies", "POST", {
                        "name": "本地验收资料只读摘要", "capabilities": ["summarize"],
                        "allowed_root_ids": [], "allowed_task_categories": [], "allowed_file_types": [],
                        "allow_restricted": False, "active": True})
                    self.checked("administrator-readonly-ai-configuration", {"policy_id": policy["id"]})
                answer = self.request("/api/v1/ai/query", "POST", {"capability": "summarize", "instruction": "请用一句中文概括事项的目的，不超过三十字。", "task_ids": [self.state["task_id"]], "file_ids": [], "confirm_external": False})
                require(bool(answer["content"].strip()) and bool(answer["sources"]), "LOCAL_LLM_EMPTY_OUTPUT")
                status = self.request("/api/v1/ai/runtime/status")
                self.checked("real-local-llm-inference", {"answer": answer, "status": status})
                self.require_models_available(status)
        self.report["network"] = "Guest QEMU restrict=on; no external provider configured"


def exercise(target, phase, *, lab=None):
    if lab is None:
        matrix, media = load_configuration()
        lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    matrix = lab.matrix
    require(matrix["targets"][target]["os"] == "windows", "WINDOWS_TARGET_REQUIRED")
    system = probe(lab, target)
    expected_installed = phase != "same-package-reinstall"
    require(system.get("installed_package") is expected_installed,
            "REINSTALL_REQUIRES_PACKAGE_ABSENT" if not expected_installed else "ACTUAL_PACKAGE_INSTALL_REQUIRED")
    packages, _ = inventory(matrix, REPO / "artifacts")
    package_id = next(key for key, value in matrix["packages"].items() if target in value["required_targets"])
    package = bind_package(lab, packages[package_id], fingerprint())
    context = {"target": target, "package": package, "environment": runtime_binding(lab, target),
               "restore_generation": lab.state(target).get("restore_generation")}
    pointer = lab.root / "state" / ("business-" + target + ".json")
    if phase == "configure":
        remote = "lifecycle-" + uuid.uuid4().hex[:12]
    else:
        previous = json.loads(pointer.read_text(encoding="utf-8"))
        require(previous["context"] == context, "BUSINESS_CONTEXT_CHANGED_RESTART_FROM_CLEAN_BASELINE")
        remote = previous["remote"]
    write_json(pointer, {"context": context, "remote": remote, "last_phase": phase, "updated_at": now()})
    local = lab.root / "reports" / target / ("business-" + phase + "-" + uuid.uuid4().hex[:12])
    local.mkdir(parents=True)
    write_json(local / "context.json", {**context, "guest_identity": system, "controller_sha256": sha256(Path(__file__)),
                                      "business_script_sha256": sha256(HERE / "guest/linux-business-lifecycle.py")})
    client = windows.connect(lab, matrix["targets"][target])
    try:
        with client.open_sftp() as fs:
            fs.put(str(HERE / "guest/windows-standard-user.ps1"), "/C:/ProgramData/PartyOps-VM-Lab/windows-standard-user.ps1")
        run = WindowsRun(client, lab, target, phase, package, context, remote, local)
        run.run()
        return {"status": "partial", "phase": phase, "report_path": str(local), "runtime_environment_passed": False}
    finally:
        client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("target")
    parser.add_argument("phase", choices=("configure", "resume-configure", "business", "after-reboot", "collaboration-business", "ocr", "models",
                                          "uninstall-keep", "same-package-reinstall", "after-reinstall", "uninstall-remove-test-data"))
    args = parser.parse_args()
    print(json.dumps(exercise(args.target, args.phase), ensure_ascii=False, indent=2))

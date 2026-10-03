"""本机 Win11 专用普通账户的安装版 HTTP 验收；不启动程序或执行 Guest 操作。

先由本机普通用户帮助脚本生成真实探针和向导回执，再提供 --run-directory、
--work、--data-directory、--identity-receipt、--wizard-receipt、--port。首次运行 configure，后续可
执行 business / collaboration-business / ocr / models；中断的首次配置只能显式 resume-configure。
所有密码仅写本轮 private/business-state.json，公开报告永不声明完整环境通过。
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import http.cookiejar
import importlib.util
import json
import ntpath
import os
import re
import secrets
import shlex
import stat
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from types import SimpleNamespace

HERE = Path(__file__).resolve().parents[1]
REPO = HERE.parent.parent
sys.path.insert(0, str(HERE))
from evidence import now, sha256, write_json
from native_install import verify as verify_install_binding
from native_user_session import verify_session
from native_windows import identity_digest, read_host
from provenance import bind_package


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


business = load_module(HERE / "guest/linux-business-lifecycle.py", "native_shared_business")
windows_runtime = load_module(HERE / "scripts/exercise-windows-business.py", "native_shared_windows_runtime")
require = business.require
REPORT_ROOT = r"D:\PartyOps-VM-Lab\reports\win11-x64-native"
WORK_ROOT = r"E:\codex\PartyOps\.partyops-vm-lab\native\win11-x64"
ACCOUNT = "PartyOpsNativeQA"
INSTALL = r"E:\PartyOps1\PartyOps"
PHASES = ("configure", "resume-configure", "revalidate", "business", "collaboration-business", "ocr", "models", "formatter-probe")


def import_retained_state(previous, context, previous_package, previous_install, account_sid, data_directory):
    """只带入已登记合成账号和对象标识，不沿用任何通过状态、进程或会话。"""
    old = previous.get("context", {})
    require(old.get("target") == context["target"] and old.get("host_id") == context["host_id"]
            and old.get("account_sid") == account_sid and path_equal(old.get("data_dir", ""), data_directory)
            and previous_install.get("package") == previous_package
            and previous_install.get("installer_exit_code") == 0
            and old.get("package_sha256") == previous_package.get("sha256")
            and old.get("source_fingerprint") == previous_package.get("source_fingerprint")
            and old.get("executable_sha256") == previous_install.get("installed_executable_sha256"),
            "NATIVE_RETAINED_STATE_BINDING_MISMATCH")
    require(re.fullmatch(r"nativeqa_[a-z0-9]+", str(previous.get("username", "")))
            and all(isinstance(previous.get(key), str) and previous[key] for key in ("password", "user_id"))
            and path_equal(previous.get("data_dir", ""), data_directory), "NATIVE_RETAINED_SYNTHETIC_ACCOUNT_MISSING")
    keys = ("task_id", "task_title", "attachment_id", "attachment_sha256")
    require(all(isinstance(previous.get(key), str) and previous[key] for key in keys),
            "NATIVE_RETAINED_BUSINESS_OBJECTS_MISSING")
    return {"context": context, "username": previous["username"], "password": previous["password"],
            "user_id": previous["user_id"], "data_dir": data_directory,
            "retained_business": {key: previous[key] for key in keys}}


def path_equal(left, right):
    return ntpath.normcase(ntpath.normpath(str(left))) == ntpath.normcase(ntpath.normpath(str(right)))


def clean_absolute(value):
    text = str(value)
    require(ntpath.isabs(text) and not text.startswith(("\\\\", "//"))
            and not any(part in {".", ".."} for part in re.split(r"[\\/]", text)), "NATIVE_PATH_NOT_CANONICAL")
    return ntpath.normpath(text)


def validate_paths(run_directory, work):
    run = clean_absolute(run_directory)
    leaf = ntpath.basename(run)
    require(path_equal(ntpath.dirname(run), REPORT_ROOT)
            and re.fullmatch(r"native-[A-Za-z0-9][A-Za-z0-9._-]{0,95}", leaf), "NATIVE_RUN_OUTSIDE_LAB")
    expected = ntpath.join(WORK_ROOT, leaf, ACCOUNT)
    require(path_equal(clean_absolute(work), expected), "NATIVE_WORKSPACE_MISMATCH")
    return expected


def validate_data_path(work, data):
    canonical = clean_absolute(data)
    require(path_equal(ntpath.dirname(canonical), work)
            and re.fullmatch(r"中文 空格业务数据-[a-f0-9]{32}", ntpath.basename(canonical)),
            "NATIVE_DATA_DIRECTORY_MISMATCH")
    return canonical


def no_reparse(path):
    # 从精确目标向上检查，拒绝把 E 盘合成路径经 junction 映射到真实业务目录。
    current = Path(path)
    for candidate in (current, *current.parents):
        try:
            value = candidate.lstat()
        except FileNotFoundError:
            continue
        require(not stat.S_ISLNK(value.st_mode)
                and not getattr(value, "st_file_attributes", 0) & 0x400, "NATIVE_REPARSE_PATH_REJECTED")


def read_json(path):
    no_reparse(path)
    value = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    require(isinstance(value, dict), "NATIVE_JSON_OBJECT_REQUIRED")
    return value


def powershell(script, payload):
    require(os.name == "nt", "NATIVE_WINDOWS_HOST_REQUIRED")
    encoded = base64.b64encode(json.dumps(payload).encode()).decode()
    # 宿主控制器使用 PowerShell 7；Windows PowerShell 5 子进程只能从系统模块目录加载 ACL 等命令。
    prefix = ("$ErrorActionPreference='Stop'\n"
              "$env:PSModulePath=(Join-Path $env:WINDIR 'System32\\WindowsPowerShell\\v1.0\\Modules')+';'"
              "+(Join-Path $env:ProgramFiles 'WindowsPowerShell\\Modules')\n"
              "[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)\n"
              "$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "'))|ConvertFrom-Json\n")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                             base64.b64encode((prefix + script).encode("utf-16le")).decode()],
                            capture_output=True, text=True, encoding="utf-8-sig", errors="strict", timeout=45, check=False,
                            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    # 任意 PowerShell/HTTP 诊断不得把其他用户配置或测试密码复制到公开报告。
    require(result.returncode == 0, "NATIVE_READONLY_PROBE_FAILED")
    return json.loads(result.stdout)


LISTENER_PROBE = r"""
$account=Get-LocalUser -Name $request.username
$admins=@(Get-LocalGroupMember -SID 'S-1-5-32-544'|ForEach-Object {$_.SID.Value})
$listeners=@(Get-NetTCPConnection -State Listen -LocalPort $request.port -ErrorAction SilentlyContinue)
$items=@($listeners|ForEach-Object {
  $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+$_.OwningProcess)
  $owner=Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid
  if($owner.ReturnValue -ne 0){throw 'NATIVE_PROCESS_OWNER_UNAVAILABLE'}
  [ordered]@{address=$_.LocalAddress;port=[int]$_.LocalPort;pid=[int]$process.ProcessId;
    owner_sid=$owner.Sid;session_id=[int]$process.SessionId;executable_path=$process.ExecutablePath;
    created_at=$process.CreationDate.ToUniversalTime().ToString('o');
    sha256=(Get-FileHash -LiteralPath $process.ExecutablePath -Algorithm SHA256).Hash.ToLowerInvariant()}
})
[ordered]@{username=$account.Name;sid=$account.SID.Value;enabled=$account.Enabled;
  administrator=($admins -contains $account.SID.Value);listeners=$items}|ConvertTo-Json -Depth 6 -Compress
"""


def validate_listener(snapshot, account, executable, checksum, port, pid=None, created_at=None):
    require(snapshot.get("username") == ACCOUNT and snapshot.get("sid") == account["sid"]
            and snapshot.get("enabled") is True and snapshot.get("administrator") is False,
            "NATIVE_STANDARD_ACCOUNT_CHANGED")
    listeners = snapshot.get("listeners", [])
    require(len(listeners) == 1, "NATIVE_LISTENER_NOT_UNIQUE")
    actual = listeners[0]
    require(actual.get("address") == "127.0.0.1" and actual.get("port") == port,
            "NATIVE_LISTENER_NOT_LOOPBACK")
    require(actual.get("owner_sid") == account["sid"] and actual.get("session_id") == account["session_id"]
            and actual.get("session_id", 0) > 0, "NATIVE_LISTENER_OWNER_MISMATCH")
    require(path_equal(actual.get("executable_path", ""), executable) and actual.get("sha256") == checksum,
            "NATIVE_INSTALLED_PROCESS_MISMATCH")
    require(isinstance(actual.get("pid"), int) and actual["pid"] > 0 and actual.get("created_at"),
            "NATIVE_PROCESS_IDENTITY_MISSING")
    if pid is not None:
        require(actual["pid"] == pid, "NATIVE_LISTENER_PID_CHANGED")
    if created_at is not None:
        # PowerShell 7 重新序列化 UTC 日期会去掉小数末尾的零；保留全部七位精度比较同一时刻。
        def utc_stamp(value):
            match = re.fullmatch(r"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?Z", str(value))
            return (match[1], (match[2] or "").ljust(7, "0")) if match else None
        stamp = utc_stamp(actual["created_at"])
        require(stamp is not None and stamp == utc_stamp(created_at), "NATIVE_LISTENER_PROCESS_REPLACED")
    return actual


def loopback_origin(url):
    parsed = urllib.parse.urlsplit(url)
    require(parsed.scheme == "http" and parsed.hostname == "127.0.0.1" and parsed.port
            and 1024 <= parsed.port <= 65534 and not parsed.username and not parsed.password
            and parsed.path in {"", "/"} and not parsed.query and not parsed.fragment,
            "NATIVE_HTTP_ORIGIN_REJECTED")
    return f"http://127.0.0.1:{parsed.port}"


class GuardedRedirect(urllib.request.HTTPRedirectHandler):
    def __init__(self, run):
        super().__init__()
        self.run = run

    def redirect_request(self, request, fp, code, message, headers, newurl):
        self.run.guard_url(newurl)
        return super().redirect_request(request, fp, code, message, headers, newurl)


def protect_private(directory):
    no_reparse(directory)
    directory.mkdir(exist_ok=True)
    result = powershell(r"""
$acl=New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
foreach($value in @('S-1-5-32-544','S-1-5-18')) {
  $sid=New-Object Security.Principal.SecurityIdentifier($value)
  $rule=New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
  $acl.AddAccessRule($rule)
}
[IO.Directory]::SetAccessControl($request.directory,$acl)
@{protected=(Get-Acl -LiteralPath $request.directory).AreAccessRulesProtected}|ConvertTo-Json -Compress
""", {"directory": str(directory)})
    require(result.get("protected") is True, "NATIVE_PRIVATE_ACL_FAILED")


class NativeRun:
    """只绑定共享 HTTP 断言，不继承 Guest 的启动、卸载、删除或重启方法。"""
    checked = business.GuestRun.checked
    login = business.GuestRun.login
    task = business.GuestRun.task
    check_business_data = business.GuestRun.check_business_data
    validate_collaboration_visibility = staticmethod(business.GuestRun.validate_collaboration_visibility)
    collaboration_business = business.GuestRun.collaboration_business
    business = business.GuestRun.business
    def __init__(self, args):
        self.args = args
        workspace = validate_paths(args.run_directory, args.work)
        self.run_directory, self.work = Path(args.run_directory), Path(args.work)
        self.revalidation = (self.run_directory / "native-user-session.json").exists()
        self.data_dir = (clean_absolute(args.data_directory) if self.revalidation
                         else validate_data_path(workspace, args.data_directory))
        for path in (self.run_directory, self.work, self.data_dir):
            no_reparse(path)
        require(self.run_directory.is_dir() and self.work.is_dir(), "NATIVE_PREPARATION_MISSING")
        self.install = read_json(self.run_directory / "install-result.json")
        self.package = read_json(self.run_directory / "candidate.json")
        require(self.install.get("target") == "win11-x64-native"
                and self.install.get("environment_type") == "native-host"
                and self.install.get("installer_exit_code") == 0
                and self.install.get("package") == self.package
                and self.package.get("id") == "windows_amd64"
                and self.package.get("provenance_status") == "verified", "NATIVE_INSTALLED_CANDIDATE_UNPROVEN")
        require(path_equal(self.install.get("install_dir", ""), INSTALL)
                and path_equal(self.install.get("registered_path", ""), INSTALL)
                and self.install.get("registered_version") == self.package.get("version"),
                "NATIVE_INSTALL_REGISTRATION_MISMATCH")
        gate = load_module(REPO / "scripts/verify-full-function-gate.py", "native_current_fingerprint")
        source = gate.source_fingerprint(REPO, "package")[0]
        lab = SimpleNamespace(root=Path("D:/PartyOps-VM-Lab"), matrix={"defaults": {
            "fallback_root": str(Path(WORK_ROOT).parents[1])}})
        bound = bind_package(lab, self.package, source)
        require(bound == self.package, "NATIVE_CANDIDATE_BINDING_CHANGED")
        self.executable = Path(INSTALL) / "PartyOps.exe"
        no_reparse(self.executable)
        require(sha256(self.executable) == self.install.get("installed_executable_sha256"),
                "NATIVE_INSTALLED_EXECUTABLE_CHANGED")
        self.system = read_host()
        require(self.system["os_release"] == "11" and self.system["arch"] == "x86_64", "NATIVE_WIN11_X64_REQUIRED")
        self.machine = powershell("[ordered]@{hardware_uuid=(Get-CimInstance Win32_ComputerSystemProduct).UUID;"
                                  "machine_name=$env:COMPUTERNAME}|ConvertTo-Json -Compress", {})
        self.identity_receipt = self.owned_receipt(args.identity_receipt)
        self.account = self.validate_identity(self.identity_receipt)
        self.config = Path(self.account["local_appdata"]) / "PartyOps"
        no_reparse(self.config)
        self.session = None
        self.wizard_origin, self.wizard = None, {}
        self.runtime_receipt = None
        if self.revalidation:
            require(args.phase not in {"configure", "resume-configure"} and args.runtime_receipt
                    and not args.wizard_receipt, "NATIVE_REVALIDATION_REQUIRES_RUNTIME_RECEIPT")
            binding = verify_install_binding(lab, self.run_directory, source)
            self.session = verify_session(lab, self.run_directory, binding, self.install, self.account)
            require(self.session["data_mode"] == "Retain" and path_equal(self.session["data_directory"], self.data_dir),
                    "NATIVE_REVALIDATION_RETAIN_DATA_REQUIRED")
            require(self.account.get("native_user_session_sha256") == self.session["sha256"],
                    "NATIVE_REVALIDATION_PROBE_SESSION_CHANGED")
            receipt = self.owned_receipt(args.runtime_receipt)
            self.validate_identity(receipt)
            require(receipt.get("native_user_session_sha256") == self.session["sha256"]
                    and receipt.get("data_mode") == "Retain", "NATIVE_REVALIDATION_RUNTIME_SESSION_CHANGED")
            self.runtime_receipt = receipt.get("runtime", {})
            require(self.runtime_receipt.get("launcher_exit_code") == 0
                    and self.runtime_receipt.get("port") == args.port
                    and self.runtime_receipt.get("created_at") and self.runtime_receipt.get("started_at"),
                    "NATIVE_REVALIDATION_RUNTIME_NOT_STARTED")
        else:
            require(args.wizard_receipt and not args.runtime_receipt, "NATIVE_FIRST_RUN_REQUIRES_WIZARD")
            wizard_receipt = self.owned_receipt(args.wizard_receipt)
            self.wizard_origin = self.validate_wizard(wizard_receipt)
            self.wizard = wizard_receipt["wizard"]
        require(1024 <= args.port <= 65534 and args.port != urllib.parse.urlsplit(self.wizard_origin).port,
                "NATIVE_RUNTIME_PORT_INVALID")
        self.origin = f"http://127.0.0.1:{args.port}"
        self.context = {"target": "win11-x64-native", "host_id": self.system["host_id"],
                        "identity_sha256": identity_digest(self.system), "boot_id": self.system["boot_id"],
                        "package_sha256": self.package["sha256"], "source_fingerprint": source,
                        "executable_sha256": self.install["installed_executable_sha256"],
                        "account_sid": self.account["sid"], "data_dir": self.data_dir,
                        "identity_receipt_sha256": sha256(Path(args.identity_receipt)),
                        "wizard_receipt_sha256": sha256(Path(args.wizard_receipt)) if args.wizard_receipt else None, "port": args.port}
        if self.session:
            self.context.update(native_user_session_sha256=self.session["sha256"],
                                runtime_receipt_sha256=sha256(Path(args.runtime_receipt)))
        self.reports = self.run_directory / "business"
        no_reparse(self.reports)
        self.reports.mkdir(exist_ok=True)
        private = self.run_directory / "private"
        protect_private(private)
        self.state_path = private / "business-state.json"
        self.state = read_json(self.state_path) if self.state_path.exists() else {}
        if self.state:
            require(self.state.get("context") == self.context, "NATIVE_BUSINESS_CONTEXT_CHANGED")
            if self.session and args.phase != "revalidate":
                require(self.state.get("revalidation_verified") is True, "NATIVE_RETAINED_OBJECTS_NOT_REVALIDATED")
        elif args.phase not in {"configure", "revalidate"}:
            raise RuntimeError("NATIVE_CONFIGURATION_STATE_MISSING")
        self.report = {"scope": "native-standard-user-installed-business", "phase": args.phase,
                       "generated_at": now(), "environment_type": "native-host", "context": self.context,
                       "status": "failed", "runtime_environment_passed": False, "checks": [],
                       "limitations": ["非干净系统安装", "未验证旧发行包覆盖升级", "未验证真实系统重启",
                                       "双账号协作不替代独立协同机入网", "未声明完整功能或全生命周期通过"]}
        self.cookies = http.cookiejar.CookieJar()
        self.opener = self.make_opener(self.cookies)

    def owned_receipt(self, path):
        absolute = clean_absolute(path)
        require(path_equal(ntpath.commonpath([absolute, str(self.run_directory)]), self.run_directory)
                and not path_equal(absolute, self.run_directory), "NATIVE_RECEIPT_OUTSIDE_RUN")
        return read_json(path)

    def validate_identity(self, receipt):
        # 契约由本机标准账户帮助脚本提供；仅自检成功不等同真实向导启动。
        require(receipt.get("status") == "passed" and receipt.get("username") == ACCOUNT
                and receipt.get("is_admin") is False and receipt.get("session_id", 0) > 0,
                "NATIVE_STANDARD_PROBE_NOT_PASSED")
        require(re.fullmatch(r"S-1-5-21-(?:\d+-){3}\d+", receipt.get("sid", "")), "NATIVE_LOCAL_SID_REQUIRED")
        require(path_equal(receipt.get("native_workspace", ""), self.work)
                and path_equal(receipt.get("run_directory", ""), self.run_directory)
                and path_equal(receipt.get("data_directory", ""), self.data_dir)
                and receipt.get("app_sha256") == self.install["installed_executable_sha256"]
                and receipt.get("install_result_sha256") == sha256(self.run_directory / "install-result.json"),
                "NATIVE_STANDARD_PROBE_BINDING_MISMATCH")
        require(str(receipt.get("hardware_uuid", "")).casefold() == self.machine["hardware_uuid"].casefold()
                and str(receipt.get("machine_name", "")).casefold() == self.machine["machine_name"].casefold(),
                "NATIVE_STANDARD_PROBE_HOST_MISMATCH")
        profile = clean_absolute(receipt.get("user_profile", ""))
        require(ntpath.basename(profile).casefold() == ACCOUNT.casefold()
                and path_equal(receipt.get("local_appdata", ""), ntpath.join(profile, "AppData", "Local")),
                "NATIVE_STANDARD_PROFILE_MISMATCH")
        return receipt

    def validate_wizard(self, receipt):
        self.validate_identity(receipt)
        wizard = receipt.get("wizard", {})
        require(wizard.get("owner_sid") == self.account["sid"]
                and wizard.get("session_id") == self.account["session_id"]
                and path_equal(wizard.get("executable_path", ""), ntpath.join(INSTALL, "PartyOpsWizard.exe"))
                and isinstance(wizard.get("pid"), int) and wizard["pid"] > 0 and wizard.get("created_at"),
                "NATIVE_WIZARD_RECEIPT_MISMATCH")
        marker = self.config / "wizard.url"
        no_reparse(marker)
        require(path_equal(wizard.get("url_file", ""), marker), "NATIVE_WIZARD_MARKER_PATH_CHANGED")
        origin = loopback_origin(wizard.get("url", ""))
        if marker.exists():
            require(loopback_origin(marker.read_text(encoding="utf-8-sig").strip()) == origin,
                    "NATIVE_WIZARD_MARKER_CHANGED")
        else:
            require(self.args.phase != "configure", "NATIVE_WIZARD_MARKER_MISSING")
        no_reparse(wizard["executable_path"])
        require(sha256(Path(wizard["executable_path"])) == wizard.get("sha256"), "NATIVE_WIZARD_EXECUTABLE_CHANGED")
        return origin

    def persist(self):
        no_reparse(self.state_path)
        write_json(self.state_path, self.state)

    def make_opener(self, cookies):
        return urllib.request.build_opener(urllib.request.ProxyHandler({}), GuardedRedirect(self),
                                          urllib.request.HTTPCookieProcessor(cookies))

    def inspect_listener(self, port):
        return powershell(LISTENER_PROBE, {"username": ACCOUNT, "port": port})

    def configuration_values(self):
        mode = read_json(self.config / "mode.json")
        config = self.config / "personal.env"
        require(mode.get("mode") == "personal" and path_equal(mode.get("config_path", ""), config),
                "NATIVE_PERSONAL_MODE_CHANGED")
        no_reparse(config)
        values = {}
        for line in config.read_text(encoding="utf-8-sig").splitlines():
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            key, separator, value = line.partition("=")
            require(bool(separator) and key not in values, "NATIVE_PERSONAL_CONFIG_INVALID")
            parsed = shlex.split(value)
            require(len(parsed) <= 1, "NATIVE_PERSONAL_CONFIG_INVALID")
            values[key] = parsed[0] if parsed else ""
        require(path_equal(values.get("PARTYOPS_DATA_DIR", ""), self.data_dir)
                and values.get("PARTYOPS_MODE") == "personal"
                and values.get("PARTYOPS_ENVIRONMENT") == "production"
                and values.get("PARTYOPS_PORT") == str(self.args.port)
                and values.get("PARTYOPS_BIND_HOST") == "127.0.0.1"
                and values.get("PARTYOPS_TLS_ENABLED") == "false"
                and values.get("PARTYOPS_SEED_DEMO") == "false", "NATIVE_SYNTHETIC_CONFIGURATION_CHANGED")
        no_reparse(self.data_dir)
        return values

    def guard_url(self, url):
        parsed = urllib.parse.urlsplit(url)
        origin = loopback_origin(urllib.parse.urlunsplit((parsed.scheme, parsed.netloc, "", "", "")))
        require(origin in {self.origin, self.wizard_origin} and not parsed.fragment,
                "NATIVE_HTTP_DESTINATION_REJECTED")
        port = parsed.port
        if origin == self.wizard_origin:
            require(parsed.path in {"", "/"} and not parsed.query, "NATIVE_WIZARD_ENDPOINT_REJECTED")
            return validate_listener(self.inspect_listener(port), self.account, self.wizard["executable_path"],
                                     self.wizard["sha256"], port, self.wizard["pid"], self.wizard["created_at"])
        self.configuration_values()
        marker = read_json(Path(self.data_dir) / ".partyops-personal-process.json")
        require(marker.get("format_version") == 1 and isinstance(marker.get("pid"), int) and marker["pid"] > 0
                and path_equal(marker.get("executable", ""), self.executable),
                "NATIVE_DATA_PROCESS_MARKER_CHANGED")
        actual = validate_listener(self.inspect_listener(port), self.account, self.executable,
                                   self.install["installed_executable_sha256"], port, marker.get("pid"))
        receipt = getattr(self, "runtime_receipt", None)
        if receipt:
            validate_listener({"username": ACCOUNT, "sid": self.account["sid"], "enabled": True,
                               "administrator": False, "listeners": [actual]}, self.account, self.executable,
                              self.install["installed_executable_sha256"], port, receipt.get("pid"), receipt.get("created_at"))
            require(receipt.get("owner_sid") == actual["owner_sid"]
                    and receipt.get("session_id") == actual["session_id"]
                    and path_equal(receipt.get("executable_path", ""), actual["executable_path"])
                    and receipt.get("sha256") == actual["sha256"], "NATIVE_REVALIDATION_PROCESS_RECEIPT_MISMATCH")
        bound = self.state.get("runtime_process")
        if bound:
            require(actual == bound, "NATIVE_RUNTIME_PROCESS_CHANGED")
        else:
            self.state["runtime_process"] = actual
            self.persist()
        return actual

    def request(self, path, method="GET", data=None, base=None, content_type=None, extra_headers=None):
        require(path.startswith("/") and not path.startswith("//"), "NATIVE_HTTP_PATH_REJECTED")
        url = (base or self.origin) + path
        self.guard_url(url)
        headers = {"Origin": base or self.origin, **(extra_headers or {})}
        for cookie in self.cookies:
            if cookie.name == "partyops_csrf":
                headers["X-PartyOps-CSRF"] = cookie.value
        if isinstance(data, dict):
            data = json.dumps(data, ensure_ascii=False).encode("utf-8")
            content_type = "application/json"
        if content_type:
            headers["Content-Type"] = content_type
        request = urllib.request.Request(url, data=data, method=method, headers=headers)
        try:
            with self.opener.open(request, timeout=240) as response:
                body = response.read()
                return json.loads(body) if "application/json" in response.headers.get("Content-Type", "") else body
        except urllib.error.HTTPError as exc:
            # 不保存可能回显密码的响应正文；404 仍交由共享备份恢复断言核对。
            exc.close()
            raise urllib.error.HTTPError(url, exc.code, "NATIVE_HTTP_STATUS", {}, None) from None

    def wait_health(self):
        # 向导提交已同步等待实际运行实例就绪；身份不符时不能用重试掩盖。
        result = self.request("/api/v1/health")
        require(result.get("status") == "ok" and result.get("app_version") == self.package["version"],
                "NATIVE_HEALTH_VERSION_MISMATCH")
        return result

    def assert_no_system_role_change(self):
        root = Path("C:/ProgramData/PartyOps")
        require(not (root / "host-switch-pending.json").exists(), "NATIVE_SYSTEM_ROLE_TRANSACTION_PENDING")
        mode = root / "mode.json"
        require((read_json(mode).get("mode") in {"personal", "client"}) if mode.exists()
                else not (root / "partyops.env").exists(), "NATIVE_EXISTING_HOST_ROLE_WOULD_CHANGE")

    def configure(self):
        require(not self.state and not (self.config / "mode.json").exists()
                and not (self.config / "personal.env").exists(), "FIRST_CONFIGURATION_REQUIRES_CLEAN_USER")
        data = Path(self.data_dir)
        require(not data.exists() or not any(data.iterdir()), "NATIVE_SYNTHETIC_DATA_NOT_EMPTY")
        self.assert_no_system_role_change()
        self.require_unused_personal_ports()
        # 先持久化合成凭据，失败后只允许同一状态 resume，不会猜测或重置已有账号。
        self.state = {"context": self.context, "username": "nativeqa_" + secrets.token_hex(5),
                      "password": secrets.token_urlsafe(32), "data_dir": self.data_dir}
        self.persist()
        self.finish_configuration(resume=False)

    def revalidate(self):
        require(self.session and self.session["data_mode"] == "Retain", "NATIVE_RETAIN_SESSION_REQUIRED")
        if not self.state:
            original = Path(self.session["original_run"])
            previous_path = original / "private/business-state.json"
            previous = read_json(previous_path)
            self.state = import_retained_state(previous, self.context, read_json(original / "candidate.json"),
                                               read_json(original / "install-result.json"), self.account["sid"], self.data_dir)
            self.state["retained_source"] = {"run": str(original), "state_sha256": sha256(previous_path)}
            self.persist()
        require(not self.state.get("revalidation_verified"), "NATIVE_REVALIDATION_ALREADY_COMPLETED")
        self.wait_health()
        require(self.request("/api/v1/bootstrap/status").get("configured") is True, "NATIVE_RETAINED_ACCOUNT_NOT_CONFIGURED")
        user = self.login()
        require(user["id"] == self.state["user_id"], "ACCOUNT_NOT_PRESERVED")
        retained = self.state["retained_business"]
        task = self.request("/api/v1/tasks/" + retained["task_id"])
        require(task["title"] == retained["task_title"], "TASK_NOT_PRESERVED")
        content = self.request("/api/v1/attachments/" + retained["attachment_id"] + "/download")
        require(hashlib.sha256(content).hexdigest() == retained["attachment_sha256"], "ATTACHMENT_NOT_PRESERVED")
        self.state["revalidation_verified"] = True
        self.persist()
        self.checked("retained-account-task-attachment-on-new-installed-runtime", {
            "user_id": user["id"], "task_id": task["id"], "attachment_sha256": retained["attachment_sha256"],
            "process": self.state["runtime_process"], "previous_evidence_reaccepted": False})

    def upload_input(self, endpoint, path, checksum):
        # 复用分块 multipart，仅通过本类每次重新核验身份的回环 HTTP 请求上传。
        return windows_runtime.WindowsRun.upload_input(self, endpoint, path, checksum)

    require_models_available = staticmethod(windows_runtime.WindowsRun.require_models_available)

    def ocr(self):
        self.wait_health()
        self.login()
        checksum = "070bd312f8654446bcca77c4efaea6df05e6a6ffba631da71e542bacb112d8d6"
        path = Path("D:/PartyOps-VM-Lab/reports/uos-deb-x64/ocr-input-20260906.png")
        result = self.upload_input("/api/v1/intake/parse", path, checksum)
        write_json(self.reports / "ocr-response.json", {"input_sha256": checksum, "result": result})
        require(all(word in result["extracted_text"] for word in ("支部", "党员大会", "会议记录")), "CHINESE_OCR_TEXT_MISMATCH")
        require(result["warnings"] == [], "OCR_RETURNED_WARNING")
        self.checked("actual-image-ocr", {"input_sha256": checksum, "result": result})

    def models(self):
        memory = powershell("@{free_bytes=[int64](Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory*1024}|ConvertTo-Json", {})
        # 本机模型额外预留 3 GiB；不得侵占原计划要求的 8 GiB 宿主余量。
        require(memory["free_bytes"] >= 11 * 1024**3, "NATIVE_MODELS_HOST_MEMORY_RESERVE")
        try:
            windows_runtime.WindowsRun.models(self)
        finally:
            # 共享驱动的 Guest 网络描述不能沿用到本机；本阶段只能证明实际本地模型调用。
            self.report["network"] = {"environment": "native-host", "host_outbound_isolation_verified": False,
                                      "external_provider_consent": False}

    def formatter_probe(self):
        """先核验票据指向的真实本机进程，不能把别的登录用户的引擎记为本轮通过。"""
        self.wait_health()
        self.login()
        ticket = self.request("/api/v1/official-format/local-ticket", "POST", {"origin": self.origin})
        origin = loopback_origin(ticket["local_base_url"])
        port = urllib.parse.urlsplit(origin).port
        require(port not in {self.args.port, urllib.parse.urlsplit(self.wizard_origin).port},
                "NATIVE_FORMATTER_PORT_OVERLAPS_APPLICATION")
        snapshot = self.inspect_listener(port)
        # 不保存票据或会话令牌；实际监听者信息用于解释端口冲突。
        write_json(self.reports / "formatter-listener.json", {"local_base_url": origin, "listener": snapshot})
        runtime = self.state["runtime_process"]
        actual = validate_listener(snapshot, self.account, self.executable,
                                   self.install["installed_executable_sha256"], port,
                                   runtime["pid"], runtime["created_at"])
        self.checked("ordinary-user-formatter-listener", {"local_base_url": origin, "process": actual})

    def resume_configure(self):
        require(self.state and not self.state.get("user_id"), "NATIVE_RESUME_CONFIGURATION_NOT_PENDING")
        self.assert_no_system_role_change()
        if (self.config / "mode.json").exists() and self.request("/api/v1/bootstrap/status").get("configured") is True:
            user = self.login()
            self.state["user_id"] = user["id"]
            self.persist()
            self.checked("resume-same-synthetic-admin", {"user_id": user["id"], "username": user["username"]})
            return
        self.finish_configuration(resume=True)

    def require_unused_personal_ports(self):
        for port in (self.args.port, self.args.port + 1):
            snapshot = self.inspect_listener(port)
            require(snapshot.get("sid") == self.account["sid"] and snapshot.get("administrator") is False
                    and snapshot.get("enabled") is True, "NATIVE_STANDARD_ACCOUNT_CHANGED")
            require(snapshot.get("listeners") == [], "NATIVE_REQUESTED_PORT_ALREADY_IN_USE")

    def finish_configuration(self, resume):
        page = self.request("/", base=self.wizard_origin).decode("utf-8")
        csrf = re.search(r'name="csrf" value="([^"]+)"', page)
        require(csrf is not None, "WIZARD_FORM_MISSING")

        def form(values):
            return self.request("/", "POST", urllib.parse.urlencode({**values, "csrf": csrf[1]}).encode(),
                                base=self.wizard_origin, content_type="application/x-www-form-urlencoded")

        if resume and (self.config / "mode.json").exists():
            self.configuration_values()
        else:
            require(not (self.config / "personal.env").exists(), "NATIVE_FOREIGN_PERSONAL_CONFIG")
            self.require_unused_personal_ports()
            page = form({"mode": "personal", "port": str(self.args.port), "data_dir": self.data_dir}).decode("utf-8")
            require('name="mode" value="bootstrap_admin"' in page, "ADMIN_SETUP_FORM_MISSING")
        self.checked("ordinary-user-personal-configuration", {"data_dir": self.data_dir,
                     "health": self.wait_health(), "process": self.state["runtime_process"]})
        bootstrap = self.request("/api/v1/bootstrap/status")
        if bootstrap.get("configured") is not True:
            form({"mode": "bootstrap_admin", "username": self.state["username"],
                  "display_name": "本机原版环境 合成验收员", "password": self.state["password"]})
        else:
            require(resume, "NATIVE_UNEXPECTED_EXISTING_ADMIN")
        user = self.login()
        self.state["user_id"] = user["id"]
        self.persist()
        require(self.request("/api/v1/bootstrap/status").get("configured") is True, "BOOTSTRAP_NOT_CONFIGURED")
        self.checked("synthetic-admin-through-real-wizard", {"user_id": user["id"], "username": user["username"]})

    def run(self):
        try:
            require(self.args.phase in PHASES, "NATIVE_PHASE_NOT_ALLOWED")
            getattr(self, self.args.phase.replace("-", "_"))()
            self.report["status"] = "passed"
        except Exception as exc:  # noqa: BLE001 - 验收边界统一脱敏，仍记录失败并返回非零。
            # 只保留稳定错误码；未知异常文本可能含接口响应或密码，不进入公开输出。
            code = ("HTTP_STATUS_" + str(exc.code)) if isinstance(exc, urllib.error.HTTPError) else (
                str(exc) if isinstance(exc, RuntimeError) and re.fullmatch(r"[A-Z0-9_]+", str(exc)) else type(exc).__name__)
            self.report["error"] = code
            raise RuntimeError(code) from None
        finally:
            write_json(self.reports / (self.args.phase + ".json"), self.report)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-directory", type=Path, required=True)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--data-directory", type=Path, required=True)
    parser.add_argument("--identity-receipt", type=Path, required=True)
    parser.add_argument("--wizard-receipt", type=Path)
    parser.add_argument("--runtime-receipt", type=Path)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--phase", choices=PHASES, required=True)
    args = parser.parse_args()
    try:
        run = NativeRun(args)
        run.run()
    except Exception as exc:  # noqa: BLE001 - CLI 不输出包含请求凭据的异常详情。
        code = str(exc) if isinstance(exc, RuntimeError) and re.fullmatch(r"[A-Z0-9_]+", str(exc)) else type(exc).__name__
        print(json.dumps({"status": "failed", "error": code, "runtime_environment_passed": False}))
        return 1
    print(json.dumps({"status": "passed", "phase": args.phase, "report": str(run.reports / (args.phase + ".json")),
                      "runtime_environment_passed": False}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

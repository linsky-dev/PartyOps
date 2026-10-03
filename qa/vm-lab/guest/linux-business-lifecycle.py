"""通过安装版桌面入口、配置向导及 HTTP API 执行业务与重启持久性检查。

凭据只存 Guest 内 0600 文件；报告仅覆盖实际执行的场景，不声明完整验收。
支持原版 UOS 的 Python 3.7，不导入产品源码，也不调用测试替身。
"""
import argparse
import hashlib
import http.cookiejar
import io
import json
import os
import platform
import re
import secrets
import shutil
import sqlite3
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime, timezone
from pathlib import Path


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def require(value, message):
    if not value:
        raise RuntimeError(message)


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


class GuestRun:
    def __init__(self, args):
        self.args = args
        require(os.getuid() != 0, "STANDARD_USER_REQUIRED")
        marker = json.loads(Path("/etc/partyops-vm-lab.json").read_text())
        require(marker == {"uuid": args.uuid, "purpose": "disposable-qa"}, "GUEST_OWNERSHIP_MISMATCH")
        self.work = args.work.resolve()
        require(bool(self.work.relative_to(Path.home().resolve()).parts), "WORK_MUST_BE_GUEST_HOME_CHILD")
        require(not args.work.is_symlink(), "WORK_SYMLINK_REJECTED")
        self.work.mkdir(parents=True, exist_ok=True)
        self.reports = self.work / "evidence"
        self.reports.mkdir(exist_ok=True)
        self.state_path = self.work / "state.local.json"
        self.state = json.loads(self.state_path.read_text()) if self.state_path.exists() else {}
        if self.state:
            require(self.state["uuid"] == args.uuid and self.state["package_sha256"] == args.package_sha256,
                    "LIFECYCLE_CONTEXT_CHANGED")
        self.boot = Path("/proc/sys/kernel/random/boot_id").read_text().strip()
        self.report = {"scope": "installed-desktop-wizard-and-business-lifecycle", "phase": args.phase,
                       "generated_at": datetime.now(timezone.utc).isoformat(), "guest_uuid": args.uuid,
                       "uid": os.getuid(), "arch": platform.machine(), "boot_id": self.boot,
                       "package_sha256": args.package_sha256, "status": "failed",
                       "runtime_environment_passed": False, "checks": []}
        self.cookies = http.cookiejar.CookieJar()
        self.opener = self.make_opener(self.cookies)
        self.origin = "http://127.0.0.1:18775"

    def make_opener(self, cookies):
        """Windows 控制器复用业务断言时可绑定同一 Guest 的 SSH HTTP 通道。"""
        return urllib.request.build_opener(urllib.request.ProxyHandler({}),
                                          urllib.request.HTTPCookieProcessor(cookies))

    def persist(self):
        write_json(self.state_path, self.state)
        self.state_path.chmod(0o600)

    def request(self, path, method="GET", data=None, base=None, content_type=None, extra_headers=None):
        headers = {"Origin": self.origin}
        headers.update(extra_headers or {})
        for cookie in self.cookies:
            if cookie.name == "partyops_csrf":
                headers["X-PartyOps-CSRF"] = cookie.value
        if isinstance(data, dict):
            data = json.dumps(data, ensure_ascii=False).encode("utf-8")
            content_type = "application/json"
        if content_type:
            headers["Content-Type"] = content_type
        request = urllib.request.Request((base or self.origin) + path, data=data, method=method, headers=headers)
        try:
            # 本地模型冷启动与推理有独立产品时限；观察窗口覆盖两者，避免客户端提前中断后重复导入。
            with self.opener.open(request, timeout=360) as response:
                body = response.read()
                return json.loads(body) if "application/json" in response.headers.get("Content-Type", "") else body
        except urllib.error.HTTPError as exc:
            # 响应为服务器诊断；不记录请求体，避免包含测试密码。
            detail = exc.read().decode("utf-8", errors="replace")
            (self.reports / (self.args.phase + "-http-error.txt")).write_text(detail, encoding="utf-8")
            raise

    def checked(self, name, value):
        self.report["checks"].append({"id": name, "status": "passed", "result": value})
        write_json(self.reports / (self.args.phase + ".json"), self.report)

    def desktop_start(self):
        executable = Path("/opt/partyops/partyops")
        require(executable.is_file(), "INSTALLED_PROGRAM_MISSING")
        current = digest(executable)
        if self.state.get("executable_sha256"):
            require(current == self.state["executable_sha256"], "INSTALLED_EXECUTABLE_CHANGED")
        environment = {key: value for key, value in os.environ.items()
                       if not key.startswith(("PARTYOPS_", "PYTHON", "MONO_", "LD_LIBRARY_PATH"))}
        environment.update(DISPLAY=":0", XAUTHORITY=str(Path.home() / ".Xauthority"),
                           DBUS_SESSION_BUS_ADDRESS=f"unix:path=/run/user/{os.getuid()}/bus")
        log = self.reports / (self.args.phase + "-desktop-launch.log")
        with log.open("w", encoding="utf-8") as stream:
            process = subprocess.Popen(["/bin/bash", "/opt/partyops/desktop-launcher.sh"],
                                       stdout=stream, stderr=subprocess.STDOUT, env=environment,
                                       start_new_session=True)
        self.report["desktop_launcher_pid"] = process.pid
        self.report["executable_sha256"] = current
        return process

    def wait_health(self):
        deadline = time.monotonic() + 300
        while True:
            try:
                result = self.request("/api/v1/health")
                require(result["status"] == "ok", "HEALTH_NOT_OK")
                require(result["app_version"] == Path("/opt/partyops/VERSION").read_text().strip(), "VERSION_MISMATCH")
                return result
            except urllib.error.URLError:
                require(time.monotonic() < deadline, "INSTALLED_START_TIMEOUT")
                time.sleep(2)

    def login(self):
        self.request("/api/v1/auth/login", "POST", {"username": self.state["username"], "password": self.state["password"]})
        user = self.request("/api/v1/auth/me")
        require(user["username"] == self.state["username"], "ACCOUNT_MISMATCH")
        return user

    def configure(self):
        config = Path.home() / ".config/partyops"
        require(not (config / "mode.json").exists() and not self.state, "FIRST_CONFIGURATION_REQUIRES_CLEAN_USER")
        self.state = {"uuid": self.args.uuid, "package_sha256": self.args.package_sha256,
                      "executable_sha256": digest(Path("/opt/partyops/partyops")),
                      "username": "lifecycleqa", "password": secrets.token_urlsafe(32),
                      "data_dir": str(self.work / "中文 空格业务数据"), "boot_id_before": self.boot}
        self.persist()
        self.desktop_start()
        marker = config / "wizard.url"
        deadline = time.monotonic() + 180
        while not marker.is_file():
            require(time.monotonic() < deadline, "DESKTOP_WIZARD_MARKER_TIMEOUT")
            time.sleep(1)
        url = marker.read_text().strip()
        parsed = urllib.parse.urlparse(url)
        require(parsed.hostname == "127.0.0.1" and parsed.scheme == "http", "WIZARD_NOT_LOOPBACK")
        html = self.request("/", base=url).decode("utf-8")
        csrf = re.search(r'name="csrf" value="([^"]+)"', html)
        require(csrf is not None, "WIZARD_FORM_MISSING")
        self.checked("standard-user-desktop-wizard", {"url": url, "page_title": re.findall(r"<title>(.*?)</title>", html)})

        def form(values):
            values["csrf"] = csrf[1]
            return self.request("/", "POST", urllib.parse.urlencode(values).encode(), base=url,
                                content_type="application/x-www-form-urlencoded")

        page = form({"mode": "personal", "port": "18775", "data_dir": self.state["data_dir"]}).decode("utf-8")
        require('name="mode" value="bootstrap_admin"' in page, "ADMIN_SETUP_FORM_MISSING")
        self.checked("personal-configuration", {"data_dir": self.state["data_dir"], "health": self.wait_health()})
        form({"mode": "bootstrap_admin", "username": self.state["username"],
              "display_name": "全生命周期 验收员", "password": self.state["password"]})
        user = self.login()
        self.state["user_id"] = user["id"]
        self.persist()
        require(self.request("/api/v1/bootstrap/status")["configured"] is True, "BOOTSTRAP_NOT_CONFIGURED")
        self.checked("first-admin-created-through-wizard", {"user_id": user["id"], "username": user["username"]})

    def task(self, title):
        return self.request("/api/v1/tasks", "POST", {
            "title": title, "description": "原版 Guest 安装版业务验收", "task_type": "standard",
            "sensitivity": "normal", "priority": "normal", "source": "原版环境验收", "source_kind": "manual",
            "owner_id": self.state["user_id"], "reviewer_id": None, "collaborator_ids": [],
            "steps": [], "materials": [], "formal_due_at": "2026-12-01T10:00:00+08:00",
            "internal_due_at": "2026-11-30T10:00:00+08:00"})

    def check_business_data(self):
        user = self.login()
        require(user["id"] == self.state["user_id"], "ACCOUNT_NOT_PRESERVED")
        task = self.request("/api/v1/tasks/" + self.state["task_id"])
        require(task["title"] == self.state["task_title"], "TASK_NOT_PRESERVED")
        content = self.request("/api/v1/attachments/" + self.state["attachment_id"] + "/download")
        require(hashlib.sha256(content).hexdigest() == self.state["attachment_sha256"], "ATTACHMENT_NOT_PRESERVED")
        return {"user_id": user["id"], "task_id": task["id"], "attachment_sha256": self.state["attachment_sha256"]}

    def business(self):
        self.wait_health()
        self.login()
        require(not self.state.get("task_id"), "BUSINESS_ALREADY_STARTED_USE_EXISTING_EVIDENCE")
        title = "原版系统 中文 空格事项 " + secrets.token_hex(4)
        task = self.task(title)
        body = io.BytesIO()
        with zipfile.ZipFile(body, "w", zipfile.ZIP_DEFLATED) as document:
            document.writestr("[Content_Types].xml", '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
            document.writestr("_rels/.rels", '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
            document.writestr("word/document.xml", '<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>原版环境 全系列验收附件，备份及系统重启后必须保持。</w:t></w:r></w:p></w:body></w:document>')
        content = body.getvalue()
        boundary = "PartyOpsQA" + secrets.token_hex(12)
        multipart = b""
        for key, value in {"category": "过程材料", "stage": "draft", "is_final": "false",
                           "client_upload_id": "upload-quick-" + secrets.token_hex(12)}.items():
            multipart += f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode()
        multipart += f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="中文 空格会议记录.docx"\r\nContent-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document\r\n\r\n'.encode()
        multipart += content + f'\r\n--{boundary}--\r\n'.encode()
        uploaded = self.request("/api/v1/tasks/" + task["id"] + "/materials/quick-upload", "POST", multipart,
                                content_type="multipart/form-data; boundary=" + boundary)
        self.state.update(task_id=task["id"], task_title=title,
                          attachment_id=uploaded["materials"][0]["versions"][0]["id"],
                          attachment_sha256=hashlib.sha256(content).hexdigest())
        self.persist()
        self.finish_business()

    def finish_business(self):
        """复用已登记的事项与附件完成后续校验；调用方须先登录，不重复创建业务资料。"""
        require(self.state.get("task_id") and self.state.get("attachment_id")
                and not self.state.get("business_verified"), "REGISTERED_UNFINISHED_BUSINESS_REQUIRED")
        self.checked("task-attachment-account", self.check_business_data())
        for extension in ("xlsx", "docx"):
            exported = self.request("/api/v1/exports/tasks." + extension)
            path = self.reports / ("business-export." + extension)
            path.write_bytes(exported)
            require(zipfile.is_zipfile(path), "OFFICE_EXPORT_INVALID")
            self.checked("export-" + extension, {"filename": path.name, "sha256": digest(path)})
        backup = self.request("/api/v1/backups", "POST")
        require(backup["status"] == "completed", "BACKUP_NOT_COMPLETED")
        self.finish_backup(backup)

    def finish_backup(self, backup):
        """续接已确认的备份，不重复创建事项、导出或备份。"""
        verified = self.request("/api/v1/admin/backups/" + backup["id"] + "/verify", "POST")
        require(verified["valid"] is True, "BACKUP_INVALID")
        downloaded = self.request("/api/v1/backups/" + backup["id"] + "/download")
        require(hashlib.sha256(downloaded).hexdigest() == verified["sha256"], "BACKUP_DOWNLOAD_HASH_MISMATCH")
        self.checked('backup-download-integrity', {'backup_id': backup['id'], 'sha256': verified['sha256']})
        extra = self.backup_extra_task()
        self.checked('post-backup-extra-task', {'task_id': extra['id']})
        restored = self.request("/api/v1/admin/backups/restore?backup_id=" + backup["id"], "POST")
        require(restored["restored"] is True, "BACKUP_RESTORE_FAILED")
        retained = self.check_business_data()
        try:
            self.request("/api/v1/tasks/" + extra["id"])
        except urllib.error.HTTPError as exc:
            require(exc.code == 404, "RESTORE_DELETION_UNEXPECTED_RESPONSE")
        else:
            raise RuntimeError("BACKUP_RESTORE_DID_NOT_ROLL_BACK_CHANGE")
        self.state["business_verified"] = True
        self.persist()
        self.checked("business-backup-restore", {"backup_id": backup["id"], "backup_sha256": verified["sha256"],
                                                 "post_backup_change_removed": True, "preserved": retained})

    def backup_extra_task(self):
        return self.task("备份之后新增：恢复时必须消失 " + secrets.token_hex(4))

    def after_reboot(self):
        require(self.state.get("business_verified"), "BUSINESS_PRECONDITION_MISSING")
        require(self.boot != self.state["boot_id_before"], "PROCESS_RESTART_IS_NOT_GUEST_REBOOT")
        self.desktop_start()
        health = self.wait_health()
        retained = self.check_business_data()
        self.checked("guest-reboot-data-persistence", {"boot_id_before": self.state["boot_id_before"],
                                                       "boot_id_after": self.boot, "health": health, "preserved": retained})
        self.state["boot_id_after"] = self.boot
        self.persist()

    @staticmethod
    def validate_collaboration_visibility(summary, tasks, notifications, task_id):
        require(all(summary.get(key, 0) >= 1 for key in ("collaborating", "reviewing", "step_assigned")),
                "COLLABORATION_WORK_SUMMARY_INCOMPLETE")
        require(any(item["id"] == task_id for item in tasks.get("items", [])),
                "COLLABORATION_REVIEW_TASK_NOT_VISIBLE")
        mentions = [item for item in notifications if item.get("entity_id") == task_id
                    and item.get("notification_type") == "mention"]
        require(len(mentions) == 1, "COLLABORATION_MENTION_MISSING_OR_DUPLICATED")

    def collaboration_business(self):
        """实际安装版的双账号协作；不替代独立协同机入网与桌面验证。"""
        require(self.state.get("business_verified"), "BUSINESS_PRECONDITION_MISSING")
        self.wait_health()
        self.login()
        staff_name = "collabqa_" + secrets.token_hex(4)
        staff_password = secrets.token_urlsafe(32)
        staff = self.request("/api/v1/admin/users", "POST", {
            "username": staff_name, "display_name": "原版环境 协同验收员",
            "password": staff_password, "role": "staff"})
        self.state["collaboration_account"] = {"id": staff["id"], "username": staff_name,
                                               "password": staff_password}
        self.persist()
        task = self.request("/api/v1/tasks", "POST", {
            "title": "双账号协同核对 " + secrets.token_hex(4), "description": "隔离 Guest 合成测试资料",
            "task_type": "standard", "sensitivity": "normal", "priority": "normal",
            "source": "原版协同验收", "source_kind": "manual", "owner_id": self.state["user_id"],
            "reviewer_id": staff["id"], "collaborator_ids": [staff["id"]],
            "steps": [{"title": "协同核对", "assignee_id": staff["id"]}], "materials": [],
            "formal_due_at": "2026-12-01T10:00:00+08:00", "internal_due_at": "2026-11-30T10:00:00+08:00"})
        comment = self.request("/api/v1/tasks/" + task["id"] + "/comments", "POST", {
            "body": "请核对本次验收资料。", "mentioned_user_ids": [staff["id"]]})
        require(comment["mentioned_user_ids"] == [staff["id"]], "COLLABORATION_MENTION_CHANGED")
        current = self.request("/api/v1/tasks/" + task["id"])
        require(current["version"] == task["version"], "COMMENT_CHANGED_TASK_VERSION")
        submitted = self.request("/api/v1/tasks/" + task["id"] + "/actions", "POST", {
            "action": "submit_review", "note": "协同核对完成"}, extra_headers={"If-Match": str(task["version"])})
        require(submitted["id"] == task["id"], "COLLABORATION_SUBMISSION_WRONG_TASK")
        self.checked("collaboration-admin-submission", {"task_id": task["id"], "staff_id": staff["id"],
                                                      "comment_id": comment["id"]})
        # 独立 Cookie 容器确保第二账号不能借用管理员会话。
        admin_cookies, admin_opener = self.cookies, self.opener
        self.cookies = http.cookiejar.CookieJar()
        self.opener = self.make_opener(self.cookies)
        try:
            self.request("/api/v1/auth/login", "POST", {"username": staff_name, "password": staff_password})
            me = self.request("/api/v1/auth/me")
            require(me["id"] == staff["id"] and me["id"] != self.state["user_id"],
                    "COLLABORATION_SESSION_NOT_INDEPENDENT")
            summary = self.request("/api/v1/tasks/my-work-summary")
            tasks = self.request("/api/v1/tasks?scope=reviewing")
            notifications = self.request("/api/v1/notifications?unread_only=true")
            self.validate_collaboration_visibility(summary, tasks, notifications, task["id"])
            reply = self.request("/api/v1/tasks/" + task["id"] + "/comments", "POST", {
                "body": "协同账号已看到事项并完成核对。", "mentioned_user_ids": [self.state["user_id"]]})
            self.checked("collaboration-independent-staff-session", {"user_id": me["id"],
                "task_id": task["id"], "reply_id": reply["id"], "summary": summary,
                "separate_machine_enrollment_verified": False})
        finally:
            self.cookies, self.opener = admin_cookies, admin_opener
        self.checked("collaboration-preserves-original-business", self.check_business_data())

    def uninstall_keep(self):
        require(self.state.get("business_verified"), "BUSINESS_PRECONDITION_MISSING")
        self.checked("before-uninstall-business", self.check_business_data())
        data = Path(self.state["data_dir"])
        require(bool(data.resolve().relative_to(self.work).parts), "UNINSTALL_DATA_OUTSIDE_OWNED_WORK")
        attachments = {str(path.relative_to(data)): digest(path) for path in data.rglob("*")
                       if path.is_file() and path.stat().st_size < 1024 * 1024
                       and digest(path) == self.state["attachment_sha256"]}
        require(bool(attachments), "BUSINESS_ATTACHMENT_FILE_MISSING")
        manager = (["dpkg", "--remove", "partyops"] if shutil.which("dpkg")
                   else ["rpm", "-e", "partyops"])
        command = subprocess.run(["sudo", "-n", *manager],
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180, check=False)
        (self.reports / "uninstall-keep-package-manager.log").write_bytes(command.stdout)
        require(command.returncode == 0, "PACKAGE_MANAGER_UNINSTALL_FAILED")
        for filename, checksum in attachments.items():
            require((data / filename).is_file() and digest(data / filename) == checksum,
                    "UNINSTALL_REMOVED_BUSINESS_ATTACHMENT")
        database = data / "partyops.db"
        require(database.is_file(), "UNINSTALL_REMOVED_BUSINESS_DATABASE")
        with sqlite3.connect(database.resolve().as_uri() + "?mode=ro", uri=True) as connection:
            account = connection.execute("SELECT id, username FROM users WHERE id=?", (self.state["user_id"],)).fetchone()
            task = connection.execute("SELECT id, title FROM tasks WHERE id=?", (self.state["task_id"],)).fetchone()
        require(account == (self.state["user_id"], self.state["username"]), "UNINSTALL_CHANGED_ACCOUNT")
        require(task == (self.state["task_id"], self.state["task_title"]), "UNINSTALL_CHANGED_TASK")
        self.checked("uninstall-data-preserved", {"data_dir": str(data), "attachments": attachments,
                                                  "user_id": account[0], "task_id": task[0]})
        require(not Path("/opt/partyops/partyops").exists(), "UNINSTALL_LEFT_EXECUTABLE")
        time.sleep(3)
        remaining = []
        for proc in Path("/proc").iterdir():
            if not proc.name.isdigit():
                continue
            try:
                executable = os.readlink(str(proc / "exe"))
                if executable.startswith("/opt/partyops/"):
                    remaining.append({"pid": int(proc.name), "executable": executable})
            except (OSError, PermissionError):
                continue
        self.report["remaining_product_processes"] = remaining
        require(not remaining, "UNINSTALL_LEFT_INSTALLED_PROGRAM_RUNNING")
        self.checked("uninstall-stopped-product-processes", {"remaining": remaining})

    def after_reinstall(self):
        require(self.state.get("business_verified"), "BUSINESS_PRECONDITION_MISSING")
        self.desktop_start()
        health = self.wait_health()
        self.checked("same-package-reinstall-data-persistence", {"health": health, "preserved": self.check_business_data(),
                                                                 "upgrade_baseline_test": False})

    def uninstall_remove_test_data(self):
        # 只清除该次验收创建的业务目录；既有数据库、主机全局目录和证据都不递归处理。
        data = Path(self.state["data_dir"])
        require(not data.is_symlink(), "TEST_DATA_SYMLINK_REJECTED")
        require(data.resolve().parent == self.work and data.name == "中文 空格业务数据",
                "TEST_DATA_CLEANUP_OUTSIDE_OWNED_WORK")
        self.uninstall_keep()
        shutil.rmtree(str(data))
        require(not data.exists(), "TEST_BUSINESS_DATA_REMAINS")
        self.checked("uninstall-owned-test-data-removed", {
            "removed_data_dir": str(data), "business_database_absent": not (data / "partyops.db").exists(),
            "attachment_storage_absent": True, "evidence_preserved": self.reports.is_dir()})

    def run(self):
        try:
            getattr(self, self.args.phase.replace("-", "_"))()
            self.report["status"] = "passed"
        except Exception as exc:
            self.report["error"] = type(exc).__name__ + ": " + str(exc)
            raise
        finally:
            write_json(self.reports / (self.args.phase + ".json"), self.report)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--uuid", required=True)
    parser.add_argument("--package-sha256", required=True)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--phase", choices=("configure", "business", "after-reboot", "collaboration-business", "uninstall-keep", "after-reinstall",
                                           "uninstall-remove-test-data"), required=True)
    GuestRun(parser.parse_args()).run()


if __name__ == "__main__":
    main()

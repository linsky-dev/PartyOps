"""当前重配置向导的错误地址 QA：正式表单提交一次，只轮询同一事务。"""
from __future__ import annotations

import argparse
import html.parser
import importlib.util
import json
import re
import sys
import time
import urllib.request
import urllib.parse
import uuid
from pathlib import Path

import retention_contract as contract

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = Path(__file__).resolve().parent
LAB_ROOT = Path("D:/PartyOps-VM-Lab")
sys.path.insert(0, str(ROOT / "qa/vm-lab"))
INVALID_HOST = "203.0.113.250"


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def require(value, code):
    if not value: raise RuntimeError(code)


class HostForm(html.parser.HTMLParser):
    """只解析正式主机表单的隐藏CSRF，不保存或输出页面正文。"""
    def __init__(self):
        super().__init__()
        self.host_form = False
        self.csrf = None
        self.mode = None
        self.title_active = False
        self.title = ""

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == "title": self.title_active = True
        if tag == "form": self.host_form = values.get("id") == "host-form"
        if tag == "input" and self.host_form:
            if values.get("name") == "csrf":
                require(self.csrf is None, "HOST_CSRF_AMBIGUOUS")
                self.csrf = values.get("value")
            if values.get("name") == "mode": self.mode = values.get("value")

    def handle_endtag(self, tag):
        if tag == "form": self.host_form = False
        if tag == "title": self.title_active = False

    def handle_data(self, data):
        if self.title_active: self.title += data


def parse_reconfiguration_csrf(page):
    form = HostForm(); form.feed(page)
    require(form.title.strip() == "党建智办 · 重新配置运行角色", "RECONFIGURATION_PAGE_REQUIRED")
    require(form.mode == "host" and isinstance(form.csrf, str) and 16 <= len(form.csrf) <= 128
            and re.fullmatch(r"[A-Za-z0-9_-]+", form.csrf), "FORM_CSRF_INVALID")
    return form.csrf


def transaction_path(value):
    identifier = value.get("transaction_id")
    require(isinstance(identifier, str) and re.fullmatch(r"[A-Za-z0-9_-]{24}", identifier), "TRANSACTION_ID_INVALID")
    path = "/transactions/" + identifier
    require(value.get("poll_url") == path and value.get("status") == "queued", "SAME_TRANSACTION_POLL_REQUIRED")
    return path


def public_status(value):
    require(value.get("status") in {"queued", "running", "ready", "failed"}, "TRANSACTION_STATE_INVALID")
    code = value.get("code", "")
    require(isinstance(code, str) and (code == "" or re.fullmatch(r"[A-Z][A-Z0-9_]{0,63}", code)), "TRANSACTION_CODE_INVALID")
    return {"status": value["status"], "code": code}


def selected_wizard(snapshot, pid, port, since, host_qa):
    host_qa.validate_wizard(snapshot, since)
    rows = [p for p in snapshot["processes"] if p["name"] == "PartyOpsWizard.exe"]
    require(rows[0]["pid"] == pid and any(row.get("pid") == pid and row.get("port") == port
            and row.get("loopback") is True and row.get("owned_by_wizard") is True
            for row in snapshot["listeners"]), "EXACT_WIZARD_PID_PORT_REQUIRED")
    return rows[0]


def run_case(args):
    windows = load(ROOT / "qa/vm-lab/scripts/exercise-windows-business.py", "host_form_windows")
    host_qa = load(OUTPUT / "win10-host-wizard-qa.py", "host_form_identity")
    transition = load(OUTPUT / "runtime-transition.py", "host_form_retention")
    matrix, media = windows.load_configuration()
    matrix["defaults"].update(primary_root=str(LAB_ROOT), fallback_root=str(LAB_ROOT))
    lab = windows.QemuLab(matrix, LAB_ROOT, media)
    binding = contract.candidate(lab, ROOT, args.package, args.sha256)
    contract.completed_install(OUTPUT / "overlay-install", binding)
    preserved = transition.preserved_inputs()[0]
    launch = contract.read(OUTPUT / "host-wizard-launch.attempt.json")
    require(launch.get("candidate") == binding and launch.get("preserved_inputs") == preserved, "BOUND_WIZARD_LAUNCH_REQUIRED")
    attempt_file = OUTPUT / "host-form-invalid-address.attempt.json"
    require(not attempt_file.exists(), "FORM_ALREADY_ATTEMPTED_INSPECT_WITHOUT_RESUBMIT")
    require(1 <= args.wizard_port <= 65535 and args.wizard_pid > 0, "WIZARD_PID_PORT_INVALID")
    contract.bind_guest(lab, windows.qmp)
    client = windows.windows.connect(lab, matrix["targets"]["win10-x64"])
    created = None
    try:
        probe_name = "host-wizard-" + uuid.uuid4().hex[:12]
        probe_staged = False
        probe_command = "& ([scriptblock]::Create([IO.File]::ReadAllText('C:/ProgramData/PartyOps-VM-Lab/" + probe_name + "/operation.ps1',[Text.Encoding]::UTF8)))"
        def guard():
            nonlocal probe_staged
            contract.bind_guest(lab, windows.qmp)
            if not probe_staged:
                snapshot = host_qa.transfer(client, windows.windows.execute,
                    host_qa.inspect_script(binding, launch["time_utc"]), probe_name)
                probe_staged = True
            else:
                # 同一只读脚本只上传一次；轮询复用短命令，不累积临时目录。
                snapshot = json.loads(windows.windows.execute(client, probe_command, timeout=120))
            wizard = selected_wizard(snapshot, args.wizard_pid, args.wizard_port, launch["time_utc"], host_qa)
            if created is not None: require(wizard["created"] == created, "WIZARD_PROCESS_CHANGED")
            return wizard

        created = guard()["created"]
        class RejectRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *_args, **_kwargs): raise RuntimeError("FORM_REDIRECT_REJECTED")
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}),
            windows.GuestHTTPHandler(client.get_transport()), RejectRedirect())
        origin = "http://127.0.0.1:" + str(args.wizard_port)

        def request(path, data=None):
            guard()
            require(path == "/" or re.fullmatch(r"/transactions/[A-Za-z0-9_-]{24}", path), "FORM_PATH_INVALID")
            headers = {"Origin": origin}
            if data is not None: headers["Content-Type"] = "application/x-www-form-urlencoded"
            try:
                with opener.open(urllib.request.Request(origin + path, data=data, headers=headers), timeout=120) as response:
                    require(int(response.headers.get("Content-Length", "0")) < 2 * 1024**2, "FORM_RESPONSE_TOO_LARGE")
                    return response.status, response.read(2 * 1024**2)
            except Exception: raise RuntimeError("FORM_HTTP_FAILED_NO_AUTOMATIC_RESUBMIT") from None

        status, page = request("/")
        require(status == 200, "FORM_GET_FAILED")
        csrf = parse_reconfiguration_csrf(page.decode("utf-8")); del page
        body = urllib.parse.urlencode({"csrf": csrf, "mode": "host", "host": INVALID_HOST,
            "port": "18765", "data_dir": host_qa.HOST_DATA}).encode("utf-8")
        require(transition.preserved_inputs()[0] == preserved, "BASELINE_CHANGED_BEFORE_FORM")
        contract.exclusive(attempt_file, {"time_utc": contract.now(), "case": args.case, "candidate": binding,
            "wizard_pid": args.wizard_pid, "wizard_created": created, "wizard_port": args.wizard_port,
            "host": INVALID_HOST, "qa_host_data_directory": host_qa.HOST_DATA, "maximum_submission_count": 1,
            "uac_acceptance": "not-verified", "preserved_inputs": preserved})
        record = {"case": args.case, "attempt_sha256": contract.digest(attempt_file), "status": "failed",
                  "states": [], "uac_acceptance": "not-verified", "host_acceptance": "not-verified"}
        try:
            status, raw = request("/", body)
            del body, csrf
            require(status == 202, "RECONFIGURATION_TRANSACTION_NOT_ACCEPTED")
            path = transaction_path(json.loads(raw)); del raw
            # 事务ID/CSRF仅内存使用；断线后禁止以重新提交表单代替状态检查。
            deadline = time.monotonic() + 300
            while True:
                status, raw = request(path)
                require(status == 200, "TRANSACTION_POLL_FAILED")
                value = json.loads(raw); del raw
                require(value.get("transaction_id") == path.rsplit("/", 1)[1], "TRANSACTION_RESPONSE_ID_CHANGED")
                state = public_status(value); del value
                record["states"].append(state)
                contract.exclusive(OUTPUT / ("host-form-invalid-address-observation-" + uuid.uuid4().hex[:12] + ".json"), state)
                if state["status"] in {"failed", "ready"}: break
                require(time.monotonic() < deadline, "TRANSACTION_PENDING_INSPECT_WITHOUT_RESUBMIT")
                time.sleep(2)
            require(transition.preserved_inputs()[0] == preserved, "BASELINE_FILES_CHANGED_AFTER_FORM")
            record["status"] = "recorded"
            record["negative_case_failed"] = state["status"] == "failed"
            record["address_specific_rejection"] = state["status"] == "failed" and state["code"] == "BIND_ADDRESS_UNAVAILABLE"
            record["unexpected_ready"] = state["status"] == "ready"
        except Exception as error:
            record["error_type"] = type(error).__name__
            raise
        finally:
            record["completed_at"] = contract.now()
            contract.exclusive(OUTPUT / "host-form-invalid-address.result.json", record)
        return {"status": record["status"], "result": str(OUTPUT / "host-form-invalid-address.result.json"),
                "terminal": record["states"][-1], "uac_acceptance": "not-verified"}
    finally: client.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", choices=["invalid-address"], required=True)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--wizard-pid", type=int, required=True)
    parser.add_argument("--wizard-port", type=int, required=True)
    try:
        result = run_case(parser.parse_args()); print(json.dumps(result, ensure_ascii=False))
        return 0 if result["terminal"]["status"] == "failed" else 3
    except Exception as error:
        print(json.dumps({"status": "failed", "error_type": type(error).__name__, "retry": "inspect-only"}))
        return 2


if __name__ == "__main__": raise SystemExit(main())

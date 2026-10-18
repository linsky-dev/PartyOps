"""新完整候选主机/个人恢复薄适配；默认离线检查，显式动作各执行一次。"""
from __future__ import annotations

import argparse
import json
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path
from types import SimpleNamespace

import controller as c

OUTPUT = Path(__file__).resolve().parent
contract = c.contract
require = c.require
HOST_SUBMIT = "first-host-approved-existing-root-submit"
HOST_TASK = "PartyOps-QA-First-Host-20261018"
RESTORE_TASK = "PartyOps-QA-Restore-Personal-20261018"
RECORD_PREFIX = ""
HOST_PREPARE_HOOK = None
RECOVERY_SOURCE_LINK = None


def record_name(name):
    return RECORD_PREFIX + name


def helpers():
    first = c.load("new_host_first", c.OLD / "first-host-qa.py")
    host = c.load("new_host_identity", c.OLD / "win10-host-wizard-qa.py")
    form = c.load("new_host_form", c.OLD / "win10-host-form-qa.py")
    transition = c.load("new_host_transition", c.OLD / "runtime-transition.py")
    first.OUTPUT = host.OUTPUT = form.OUTPUT = transition.OUTPUT = OUTPUT
    host.HOST_DATA = first.ROOT_HOST_DATA
    host.TASK = HOST_TASK
    transition.preserved_inputs = c.boot_inputs
    return first, host, form, transition


def directory_provenance(first):
    """仅复用旧目录的真实创建/公开CA证据，不借旧候选充当新主机验收。"""
    previous = c.previous_binding()
    old_attempt = contract.read(c.OLD / "first-host-root-directory-submit.attempt.json")
    return first.approved_existing_binding(old_attempt,
        contract.read(c.OLD / "first-host-root-directory-submit.result.json"),
        contract.read(c.OLD / "host-rollback-final-state.json"),
        contract.read(c.OLD / "retention-after-host-rollback.json"),
        contract.read(c.OLD / "retention-baseline.json"), previous,
        contract.digest(c.OLD / "first-host-root-directory-submit.attempt.json"), old_attempt["preserved_inputs"])


def current_inputs(binding):
    inputs, boot = c.boot_inputs()
    require(boot.get("candidate") == binding, "NEW_CANDIDATE_BOOT_BINDING_REQUIRED")
    contract.completed_install(OUTPUT / "overlay-install", binding)
    retained = contract.read(OUTPUT / "retention-after-reboot.json")
    require(retained.get("status") == "passed" and retained.get("candidate") == binding
            and retained.get("inputs") == inputs
            and retained.get("install_completed_sha256") == contract.digest(OUTPUT / "overlay-install/completed.json"),
            "PASSED_NEW_CANDIDATE_REBOOT_RETENTION_REQUIRED")
    import reboot_evidence
    windows = c.load("host_reboot_validation", c.ROOT / "qa/vm-lab/scripts/exercise-windows-business.py")
    reboot_evidence.validate_retention(binding, retained, contract.read(contract.DISK.parent / "vm.json"),
                                      windows.restart.validate_restart, OUTPUT)
    return inputs


def host_success(value, binding, attempt_sha, first):
    status = value.get("host_status", {})
    require(value.get("candidate") == binding and value.get("attempt_sha256") == attempt_sha
            and value.get("case") == "retained-existing-root" and value.get("status") == "bootstrap-page-observed"
            and value.get("http_status") == 200 and value.get("bootstrap_admin_page") is True
            and value.get("public_error_codes") == [] and status.get("present") is True
            and status.get("stage") == "ready" and status.get("code") == ""
            and type(status.get("pid")) is int and status["pid"] > 0
            and value.get("user_mode") == value.get("system_mode") == "host"
            and value.get("qa_host_data_directory") == first.ROOT_HOST_DATA
            and value.get("original_personal_restore_directory") == first.PERSONAL_DATA,
            "SUCCESSFUL_NEW_BOUND_HOST_REQUIRED")


def launch_script(host, binding, since):
    script = host.launch_script(binding, since).replace("--reconfigure --initial-role host", "--initial-role host")
    # 不覆盖旧任务，也不重发新任务；注册前原地阻断。
    marker = "$registered=$folder.RegisterTaskDefinition"
    require(marker in script, "EXPECTED_LAUNCH_TEMPLATE_REQUIRED")
    return script.replace(marker, "if(@($folder.GetTasks(1)|Where-Object {$_.Name -eq $request.task}).Count) {throw 'HOST_TASK_EXISTS_INSPECT_ONLY'}\n" + marker, 1)


def configured_restore(first, host, form, transition, module):
    restore = c.load("new_host_restore", c.OLD / "restore-personal-qa.py")
    restore.OUTPUT = OUTPUT
    restore.host_success = host_success
    if RECORD_PREFIX:
        # 仅动作回执使用独立前缀；安装/boot/原基线仍直接引用本目录既有真实证据。
        def routed(path):
            value = Path(path)
            if value.parent == OUTPUT and value.name.startswith(("first-host-", "restore-personal-")):
                return value.with_name(record_name(value.name))
            return value
        proxy = SimpleNamespace(**{key: getattr(contract, key) for key in dir(contract) if not key.startswith("__")})
        proxy.read = lambda path: contract.read(routed(path))
        proxy.digest = lambda path: contract.digest(routed(path))
        proxy.exclusive = lambda path, value: contract.exclusive(routed(path), value)
        restore.contract = proxy
    old_load = restore.load
    mapping = {"first-host-qa.py": first, "win10-host-wizard-qa.py": host,
               "win10-host-form-qa.py": form, "runtime-transition.py": transition,
               "exercise-windows-business.py": module}
    restore.load = lambda path, name: mapping[Path(path).name] if Path(path).name in mapping else old_load(path, name)
    original_prepare = restore.prepare_script
    def prepare(*args):
        script = original_prepare(*args).replace("PartyOps-QA-Restore-Personal-20261017", RESTORE_TASK)
        if RECORD_PREFIX:
            script = script.replace('if(@(Get-CimInstance Win32_Process -Filter "Name=\'PartyOpsWizard.exe\'").Count) {throw \'OTHER_WIZARD_APPEARED\'}',
                '$liveW=@(Get-CimInstance Win32_Process -Filter "Name=\'PartyOpsWizard.exe\'"|Where-Object {Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue});if($liveW.Count) {throw \'OTHER_LIVE_WIZARD_APPEARED\'}')
        marker = "$registered=$folder.RegisterTaskDefinition"
        require(marker in script, "EXPECTED_RESTORE_TEMPLATE_REQUIRED")
        return "$ErrorActionPreference='Stop'\n" + restore.TASK_ABSENT + "\n" + script.replace(marker, "if(@($folder.GetTasks(1)|Where-Object {$_.Name -eq '" + RESTORE_TASK
            + "'}).Count) {throw 'RESTORE_TASK_EXISTS_INSPECT_ONLY'}\n" + marker, 1)
    restore.prepare_script = prepare
    restore.TASK_ABSENT = restore.TASK_ABSENT.replace("PartyOps-QA-Restore-Personal-20261017", RESTORE_TASK)
    restore.TASK_OBSERVE = restore.TASK_OBSERVE.replace("PartyOps-QA-Restore-Personal-20261017", RESTORE_TASK)
    return restore


def operation_name(action):
    return record_name({"host-prepare": "first-host-prepare", "host-submit": HOST_SUBMIT,
            "restore-prepare": "restore-personal-prepare", "restore-submit": "restore-personal-submit"}[action])


def require_personal_start(observed):
    """沿用上轮已真实恢复的个人模式；不能删系统配置伪装成未配置。"""
    require(observed.get("user_mode") == observed.get("system_mode") == "personal"
            and observed.get("wizard_exists") is False,
            "RESTORED_PERSONAL_WITHOUT_WIZARD_REQUIRED")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", nargs="?", default="check", choices=["check", "inspect", "host-prepare", "host-submit",
        "restore-prepare", "restore-submit", "verify-retention"])
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--execute", action="store_true", help="明确授权本次 Guest 探针或动作；默认不连接")
    parser.add_argument("--restore-evidence", choices=["submit", "observed-completion"], default="submit")
    args = parser.parse_args(argv)
    require(args.action == "check" or args.execute, "EXPLICIT_EXECUTE_REQUIRED")
    require(args.action != "check" or not args.execute, "CHECK_IS_OFFLINE_ONLY")
    if args.action in {"host-prepare", "host-submit", "restore-prepare", "restore-submit"}:
        c.no_attempt(OUTPUT / (operation_name(args.action) + ".attempt.json"))
        c.no_attempt(OUTPUT / (operation_name(args.action) + ".result.json"))
    module, lab, target = c.environment()
    binding = contract.candidate(lab, c.ROOT, args.package, args.sha256)
    inputs = current_inputs(binding)
    first, host, form, transition = helpers()
    provenance = directory_provenance(first)
    if args.action == "check":
        print(json.dumps({"status": "checked-offline", "candidate": binding, "inputs": inputs,
                          "host_data_directory": first.ROOT_HOST_DATA, "actions_executed": False}, ensure_ascii=False))
        return
    if args.action.startswith("restore-"):
        original_configuration = module.load_configuration
        def configuration():
            matrix, media = original_configuration()
            media.setdefault("win10-x64", {"format": "iso", "arch": "x86_64"})
            return matrix, media
        module.load_configuration = configuration
        restore = configured_restore(first, host, form, transition, module)
        action = args.action.removeprefix("restore-")
        # 仅原 prepare/submit；不暴露 recovery 动作，未知结果只检查不重放。
        result = restore.run(SimpleNamespace(action=action, package=args.package, sha256=args.sha256,
                                             use_recovered_prepare=False, use_observed_recovery=False))
        print(json.dumps(result, ensure_ascii=False))
        return
    contract.bind_guest(lab, module.qmp)
    client = module.windows.connect(lab, target)
    attempt = record = destination = None
    try:
        since = json.loads(module.windows.execute(client, "@{guest_utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Compress"))["guest_utc"]
        def snapshot():
            contract.bind_guest(lab, module.qmp)
            return host.transfer(client, module.windows.execute, first.inspection_script(host, binding, since),
                                 "host-wizard-" + uuid.uuid4().hex[:12])
        observed = snapshot()
        actual = contract.probe(client, module.windows.execute)
        c.identity_matches(actual, binding)
        if args.action == "inspect":
            contract.exclusive(OUTPUT / ("host-personal-observation-" + uuid.uuid4().hex[:12] + ".json"),
                {"candidate": binding, "time_utc": contract.now(), "snapshot": observed, "runtime": actual,
                 "scope": "read-only-observation-not-completion"})
            return
        require(not observed["pending_mode_switch"] and not observed["user_journal_present"], "PENDING_REQUIRES_READ_ONLY_INSPECT")
        if args.action == "verify-retention":
            restore_file = OUTPUT / record_name("restore-personal-submit.result.json")
            verify = c.load("new_restored_validation", c.OLD / "verify-retained-business.py")
            if args.restore_evidence == "observed-completion":
                require(RECORD_PREFIX == "after-journal-", "EXPLICIT_RECOVERED_ROUND_REQUIRED")
                adapter = c.load("retained_observed_restore", OUTPUT / "restore-after-journal.py")
                restored = adapter.validate_completion(binding)
                restore_file = OUTPUT / record_name("restore-personal-observed-completion.json")
            else:
                restored = verify.restored_personal(contract.read(restore_file), binding,
                    contract.digest(OUTPUT / record_name("restore-personal-submit.attempt.json")))
            verify.same_restored_runtime(actual, restored, binding)
            data = c.retention(module, lab, client, "after-host-restore", actual, binding)
            require(c.boot_inputs()[0] == inputs, "ORIGINAL_INPUTS_CHANGED")
            contract.exclusive(OUTPUT / record_name("retention-after-host-restore.json"), {"status": "passed", "phase": "after-host-restore",
                "candidate": binding, "inputs": inputs, "guest_uuid": contract.UUID, "disk": str(contract.DISK.resolve()),
                "runtime": actual, "personal_restore_completed_sha256": contract.digest(restore_file),
                "host_success_sha256": contract.digest(OUTPUT / record_name(HOST_SUBMIT + ".result.json")), **data})
            return
        operation = operation_name(args.action)
        attempt = OUTPUT / (operation + ".attempt.json")
        destination = OUTPUT / (operation + ".result.json")
        prepared_launch = None
        if args.action == "host-prepare":
            if HOST_PREPARE_HOOK is None: require_personal_start(observed)
            else: prepared_launch = HOST_PREPARE_HOOK(first, host, transition, binding, since, observed, actual)
            contract.runtime_matches(actual, binding)
            require(any(p.get("name") == "PartyOps.exe" and p.get("pid") == actual["processes"][0]["pid"]
                    and p.get("owner_is_original_user") is True and p.get("installed_path_matches") is True
                    and p.get("session_id") == observed["session_id"] for p in observed["processes"]),
                    "ORIGINAL_STANDARD_PERSONAL_RUNTIME_REQUIRED")
            c.retention(module, lab, client, "before-new-host", actual, binding)
            directory = host.transfer(client, module.windows.execute, first.existing_root_script(provenance),
                                      "host-wizard-" + uuid.uuid4().hex[:12])
            first.validate_existing_root(directory, provenance)
        else:
            prepared = contract.read(OUTPUT / record_name("first-host-prepare.result.json"))
            require(prepared.get("status") == "prepared" and prepared.get("candidate") == binding
                    and prepared.get("attempt_sha256") == contract.digest(OUTPUT / record_name("first-host-prepare.attempt.json")),
                    "NEW_HOST_PREPARE_REQUIRED")
            host.validate_wizard(observed, prepared["wizard"]["created"])
            rows = [p for p in observed["processes"] if p["name"] == "PartyOpsWizard.exe"]
            require(first.same_wizard(rows[0], prepared["wizard"]), "PREPARED_WIZARD_CHANGED")
            ports = {p["port"] for p in observed["listeners"] if p["owned_by_wizard"] and p["loopback"]}
            require(len(ports) == 1, "SINGLE_HOST_WIZARD_PORT_REQUIRED")
            origin = "http://127.0.0.1:" + str(ports.pop())
            class RejectRedirect(urllib.request.HTTPRedirectHandler):
                def redirect_request(self, *_args, **_kwargs): raise RuntimeError("HOST_REDIRECT_REJECTED")
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), module.GuestHTTPHandler(client.get_transport()), RejectRedirect())
            def request(body=None):
                current = snapshot()
                host.validate_wizard(current, prepared["wizard"]["created"])
                found = [p for p in current["processes"] if p["name"] == "PartyOpsWizard.exe"]
                require(first.same_wizard(found[0], prepared["wizard"]) and any(p["pid"] == found[0]["pid"]
                    and p["port"] == int(origin.rsplit(":", 1)[1]) and p["owned_by_wizard"] and p["loopback"]
                    for p in current["listeners"]), "WIZARD_HTTP_IDENTITY_CHANGED")
                headers = {"Origin": origin}
                if body is not None: headers["Content-Type"] = "application/x-www-form-urlencoded"
                try:
                    with opener.open(urllib.request.Request(origin + "/", data=body, headers=headers), timeout=600) as response:
                        return response.status, response.read(2 * 1024**2).decode("utf-8")
                except Exception: raise RuntimeError("HOST_HTTP_FAILED_INSPECT_WITHOUT_RESUBMIT") from None
            code, page = request()
            require(code == 200, "HOST_FORM_GET_FAILED")
            parser_type = first.first_form_parser(form.HostForm)
            csrf, address = first.choose_first_form(page, "10.0.2.15", parser_type)
            del page
            body = urllib.parse.urlencode({"csrf": csrf, "mode": "host", "host": address,
                "port": "18765", "data_dir": first.ROOT_HOST_DATA}).encode("utf-8")
            del csrf
            directory = host.transfer(client, module.windows.execute, first.existing_root_script(provenance),
                                      "host-wizard-" + uuid.uuid4().hex[:12])
            first.validate_existing_root(directory, provenance)
        require(c.boot_inputs()[0] == inputs, "ORIGINAL_INPUTS_CHANGED_BEFORE_ACTION")
        contract.bind_guest(lab, module.qmp)
        contract.exclusive(attempt, {"time_utc": contract.now(), "since": since, "candidate": binding,
            "action": args.action, "case": "retained-existing-root", "preserved_inputs": inputs,
            "qa_host_data_directory": first.ROOT_HOST_DATA, "original_personal_restore_directory": first.PERSONAL_DATA,
            "directory_provenance": provenance, "directory_contract": directory, "automatic_uac_acceptance": False,
            **(RECOVERY_SOURCE_LINK(binding) if RECOVERY_SOURCE_LINK else {})})
        record = {"status": "failed", "candidate": binding, "since": since, "attempt_sha256": contract.digest(attempt),
            "case": "retained-existing-root", "qa_host_data_directory": first.ROOT_HOST_DATA,
            "original_personal_restore_directory": first.PERSONAL_DATA, "account_creation": "not-attempted", "uac_acceptance": "not-verified"}
        if args.action == "host-prepare":
            record["operation"] = host.transfer(client, module.windows.execute, prepared_launch or launch_script(host, binding, since),
                                                "host-wizard-" + uuid.uuid4().hex[:12])
            require(record["operation"].get("launch_requested") is True, "HOST_LAUNCH_NOT_CONFIRMED")
            deadline = time.monotonic() + 45
            while True:
                observed = snapshot()
                try: host.validate_wizard(observed, since); break
                except RuntimeError:
                    require(time.monotonic() < deadline, "HOST_LAUNCH_TIMEOUT_INSPECT_ONLY")
                    time.sleep(2)
            record.update(status="prepared", wizard=next(p for p in observed["processes"] if p["name"] == "PartyOpsWizard.exe"))
        else:
            code, page = request(body)
            parsed = parser_type(); parsed.feed(page); del page, body
            observed = snapshot()
            record.update(http_status=code, bootstrap_admin_page=parsed.bootstrap_admin,
                public_error_codes=list(dict.fromkeys(parsed.codes)), host_status=observed["fixed_qa_host_status"],
                user_mode=observed["user_mode"], system_mode=observed["system_mode"])
            record["status"] = "bootstrap-page-observed" if code == 200 and parsed.bootstrap_admin and not parsed.codes else "host-first-run-failed"
            host_success(record, binding, contract.digest(attempt), first)
        require(c.boot_inputs()[0] == inputs, "ORIGINAL_INPUTS_CHANGED_AFTER_ACTION")
    except Exception as error:
        if record is not None: record.update(status="failed", error_type=type(error).__name__)
        raise
    finally:
        try: client.close()
        except Exception:
            if record is not None: record.update(status="failed", cleanup_error="SSH_CLOSE_FAILED")
            raise
        finally:
            if record is not None:
                record["completed_at"] = contract.now()
                contract.exclusive(destination, record)


if __name__ == "__main__":
    main()

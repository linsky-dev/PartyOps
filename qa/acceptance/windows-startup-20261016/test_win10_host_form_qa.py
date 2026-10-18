"""主机错误地址表单的纯本地契约测试，不连接 Guest。"""
import copy
import importlib.util
import json
import unittest
from pathlib import Path
from unittest.mock import MagicMock

spec = importlib.util.spec_from_file_location("win10_host_form_qa", Path(__file__).with_name("win10-host-form-qa.py"))
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)


class HostFormTests(unittest.TestCase):
    def page(self, title="重新配置运行角色", extra=""):
        return '<title>党建智办 · ' + title + '</title><form id="personal-form"><input name="csrf" value="wrong"></form>' + \
            '<form id="host-form"><input name="csrf" value="qa_secret_1234567890"><input name="mode" value="host">' + extra + '</form>'

    def test_csrf_comes_only_from_reconfiguration_host_form(self):
        self.assertEqual(adapter.parse_reconfiguration_csrf(self.page()), "qa_secret_1234567890")
        for page in (self.page("首次配置"), self.page(extra='<input name="csrf" value="duplicate">'),
                     self.page().replace('value="host"', 'value="personal"'), self.page().replace('qa_secret_1234567890', 'bad')):
            with self.assertRaises(RuntimeError): adapter.parse_reconfiguration_csrf(page)

    def test_poll_path_must_be_returned_same_transaction(self):
        row = {"transaction_id": "a" * 24, "status": "queued", "poll_url": "/transactions/" + "a" * 24}
        self.assertEqual(adapter.transaction_path(row), row["poll_url"])
        for key, value in (("transaction_id", "../bad"), ("poll_url", "http://other/transactions/id"),
                           ("poll_url", "/transactions/" + "b" * 24), ("status", "ready")):
            with self.assertRaises(RuntimeError): adapter.transaction_path({**row, key: value})

    def test_transaction_output_is_status_and_code_only(self):
        raw = {"status": "failed", "code": "BIND_ADDRESS_UNAVAILABLE", "message": "private body",
               "transaction_id": "secret", "redirect_url": "http://host/?token=secret", "password": "secret"}
        self.assertEqual(adapter.public_status(raw), {"status": "failed", "code": "BIND_ADDRESS_UNAVAILABLE"})
        self.assertNotIn("secret", json.dumps(adapter.public_status(raw)))
        for key, value in (("status", "unknown"), ("code", "PRIVATE TOKEN")):
            with self.assertRaises(RuntimeError): adapter.public_status({**raw, key: value})

    def test_exact_wizard_port_and_pid_are_required(self):
        host_qa = MagicMock()
        snapshot = {"processes": [{"name": "PartyOpsWizard.exe", "pid": 612, "created": "stamp"}],
                    "listeners": [{"pid": 612, "port": 49693, "loopback": True, "owned_by_wizard": True}]}
        self.assertEqual(adapter.selected_wizard(snapshot, 612, 49693, "since", host_qa)["pid"], 612)
        host_qa.validate_wizard.assert_called_once_with(snapshot, "since")
        for pid, port in ((613, 49693), (612, 49694)):
            with self.assertRaises(RuntimeError): adapter.selected_wizard(snapshot, pid, port, "since", host_qa)
        changed = copy.deepcopy(snapshot); changed["listeners"][0]["loopback"] = False
        with self.assertRaises(RuntimeError): adapter.selected_wizard(changed, 612, 49693, "since", host_qa)


if __name__ == "__main__": unittest.main()

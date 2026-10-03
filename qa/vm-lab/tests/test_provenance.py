"""合成构建回执反例，不代表产品包或 Guest 验收。"""
from types import SimpleNamespace

import pytest
from evidence import sha256, write_json
from provenance import bind_package


@pytest.fixture
def receipt(tmp_path):
    package = tmp_path / "fixture.deb"
    package.write_bytes(b"unit test package")
    log = tmp_path / "reports/build-test/build.log"
    log.parent.mkdir(parents=True)
    log.write_text("unit test build only")
    data = {"id": "linux_amd64", "version": "test", "sha256": sha256(package), "path": str(package)}
    record = {"package": data, "source_before": "a" * 64, "source_after": "a" * 64,
              "exit_code": 0, "output_created_during_build": True,
              "log": {"path": str(log), "sha256": sha256(log)}}
    path = tmp_path / "state/builds" / (data["sha256"] + ".json")
    write_json(path, record)
    return SimpleNamespace(root=tmp_path), data, record, path, log


def test_package_binding_uses_controller_receipt(receipt):
    lab, package, _, _, _ = receipt
    assert bind_package(lab, package, "a" * 64)["provenance_status"] == "verified"


@pytest.mark.parametrize("field,value,error", [
    ("source_before", "old", "PACKAGE_SOURCE_FINGERPRINT_MISMATCH"),
    ("source_after", "changed", "PACKAGE_SOURCE_FINGERPRINT_MISMATCH"),
    ("exit_code", 1, "PACKAGE_BUILD_NOT_PROVEN"),
    ("output_created_during_build", False, "PACKAGE_BUILD_NOT_PROVEN"),
])
def test_invalid_build_cannot_be_accepted(receipt, field, value, error):
    lab, package, record, path, _ = receipt
    record[field] = value
    write_json(path, record)
    with pytest.raises(RuntimeError, match=error):
        bind_package(lab, package, "a" * 64)


def test_changed_build_log_or_package_is_rejected(receipt):
    lab, package, _, _, log = receipt
    log.write_text("changed")
    with pytest.raises(RuntimeError, match="PACKAGE_BUILD_LOG_CHANGED"):
        bind_package(lab, package, "a" * 64)


def test_self_report_cannot_replace_missing_build_receipt(receipt):
    lab, package, _, path, _ = receipt
    path.rename(path.with_suffix(".historical"))
    package.update(provenance_status="verified", source_fingerprint="a" * 64)
    with pytest.raises(RuntimeError, match="PACKAGE_BUILD_RECEIPT_MISSING"):
        bind_package(lab, package, "a" * 64)

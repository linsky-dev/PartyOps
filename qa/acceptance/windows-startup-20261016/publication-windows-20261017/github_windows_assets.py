"""Windows四附件追加驱动；默认只本地准备，不改正文/main/tag，不删除或clobber。"""
from __future__ import annotations
import argparse
import datetime
import hashlib
import http.client
import json
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

HERE = Path(__file__).resolve().parent
BATCH = HERE / "batch"
STATE = HERE / "github-windows-assets-state"
EXPECTED = HERE / "github-windows-assets-expected.json"
SNAPSHOT = HERE / "github-current-before-20261018.json"
GH = Path("C:/Program Files/GitHub CLI/gh.exe")
REPO = "linsky-dev/PartyOps"
TAG = "v1.4.5-rc.6"
RELEASE_ID = 402021905
TAG_SHA = "a8aaf44984c1d9ce85981bd4756e71372c6b8cb8"
MAIN_SNAPSHOT = "b58cd0505ebd8bc187d40857e92d19c88b2a2b56"
NAME = "PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe"
SIZE = 449978846
SHA = "3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27"
MANIFEST = "release-manifest-windows-startup-20261017.json"
MANIFEST_SHA = "41a126b135097255e224192ca12fe6be479d077d3842f122e67b34b5c3657d1c"
GUIDE = "windows-installation-20261017.zh-CN.md"
BASE_URL = "https://partyops-rc6-win7-x86.app.workbuddy.host"


def require(value, code):
    if not value:
        raise RuntimeError(code)


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def exclusive(path, value):
    with Path(path).open("x", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def asset_identity(asset):
    return {key: asset.get(key) for key in ("id", "name", "size", "digest")}


def local_payload():
    files = [BATCH / NAME, BATCH / (NAME + ".sha256"), BATCH / MANIFEST, HERE / "docs-draft/docs" / GUIDE]
    require(all(path.is_file() and path.resolve().is_relative_to(HERE) for path in files), "FIXED_WINDOWS_ASSET_FILES_REQUIRED")
    require(files[0].stat().st_size == SIZE and digest(files[0]) == SHA, "LOCAL_WINDOWS_EXE_CHANGED")
    require(files[1].read_text(encoding="utf-8-sig").strip() == SHA + "  " + NAME, "LOCAL_WINDOWS_SIDECAR_CHANGED")
    require(digest(files[2]) == MANIFEST_SHA, "SIGNED_WINDOWS_MANIFEST_CHANGED")
    manifest = read(files[2])
    require(manifest.get("publication_authorized") is True and manifest.get("formal_release_ready") is True
            and len(manifest.get("assets", [])) == 10 and any(row.get("filename") == NAME and row.get("sha256") == SHA
            and row.get("size") == SIZE for row in manifest["assets"]), "SIGNED_WINDOWS_CANDIDATE_BINDING_REQUIRED")
    before = read(SNAPSHOT)
    old = before["release"]
    require(old.get("id") == RELEASE_ID and old.get("tag_name") == TAG and old.get("draft") is False
            and before["tag"]["object"].get("sha") == TAG_SHA and before["tag"]["object"].get("type") == "commit"
            and before["main"]["object"]["sha"] == MAIN_SNAPSHOT and len(old.get("assets", [])) == 27
            and len({row["id"] for row in old["assets"]}) == 27, "FIXED_GITHUB_BASELINE_REQUIRED")
    assets = [{"filename": path.name, "path": str(path), "bytes": path.stat().st_size, "sha256": digest(path)} for path in files]
    require(len({row["filename"] for row in assets}) == 4
            and not ({row["filename"] for row in assets} & {row["name"] for row in old["assets"]}), "APPEND_ONLY_NEW_NAMES_REQUIRED")
    return {"repository": REPO, "tag": TAG, "release_id": RELEASE_ID, "tag_sha": TAG_SHA,
            "main_snapshot_only": MAIN_SNAPSHOT, "snapshot_sha256": digest(SNAPSHOT), "package_sha256": SHA,
            "manifest_sha256": MANIFEST_SHA, "assets": assets, "old_assets": [asset_identity(row) for row in old["assets"]]}


def prepare():
    payload = local_payload()
    if EXPECTED.exists():
        require(read(EXPECTED) == payload, "PREPARED_WINDOWS_ASSETS_CHANGED")
    else:
        exclusive(EXPECTED, payload)
    return {"status": "local-prepared", "asset_count": 4, "remote_called": False,
            "domestic_readback_required_before_upload": True, "expected": str(EXPECTED)}


def checked():
    payload = local_payload()
    require(read(EXPECTED) == payload, "PREPARED_WINDOWS_ASSETS_CHANGED")
    return payload


def domestic():
    path = BATCH / "windows-domestic-readback.json"
    require(path.is_file(), "CURRENT_WINDOWS_DOMESTIC_READBACK_REQUIRED")
    proof = read(path)
    binding = read(BATCH / "binding.json")
    result = proof.get("result", {})
    require(proof.get("binding") == binding and binding.get("filename") == NAME and binding.get("bytes") == SIZE
            and binding.get("package_sha256") == SHA and binding.get("manifest_sha256") == MANIFEST_SHA
            and binding.get("formal_release_ready") is True and binding.get("publication_authorized") is True
            and proof.get("other_nine_preserved") is True and proof.get("old_windows_preserved") is True
            and proof.get("formal_release_ready") is True and proof.get("completed_at")
            and result.get("filename") == NAME and result.get("bytes") == SIZE and result.get("sha256") == SHA
            and result.get("finalized") is True and result.get("public_readback_verified") is True
            and result.get("url") == BASE_URL + "/downloads/" + NAME, "CURRENT_WINDOWS_DOMESTIC_READBACK_REQUIRED")
    return digest(path)


def gh_api(endpoint):
    """只读API，认证交给正常gh；从不查询/打印token或stderr。"""
    for attempt in range(2):
        try:
            result = subprocess.run([str(GH), "api", "--method", "GET", "-H", "Accept: application/vnd.github+json", endpoint],
                                    capture_output=True, timeout=180)
        except subprocess.TimeoutExpired:
            if attempt == 0:
                time.sleep(2); continue
            raise RuntimeError("GITHUB_READ_FAILED") from None
        if result.returncode == 0:
            try: return json.loads(result.stdout)
            except (ValueError, UnicodeError): raise RuntimeError("GITHUB_READ_JSON_INVALID") from None
        match = re.search(rb"HTTP (\d{3})", result.stderr)
        status = int(match.group(1)) if match else None
        if attempt == 0 and (status is None or status == 429 or 500 <= status <= 599):
            time.sleep(20 if status == 429 else 2); continue
        raise RuntimeError("GITHUB_READ_FAILED")


def observe(payload):
    release = gh_api("repos/" + REPO + "/releases/" + str(RELEASE_ID))
    tag = gh_api("repos/" + REPO + "/git/ref/tags/" + TAG)
    require(release.get("id") == RELEASE_ID and release.get("tag_name") == TAG and release.get("draft") is False
            and tag["object"].get("type") == "commit" and tag["object"].get("sha") == TAG_SHA, "GITHUB_RELEASE_OR_TAG_CHANGED")
    current = {row["id"]: asset_identity(row) for row in release.get("assets", [])}
    require(all(current.get(row["id"]) == row for row in payload["old_assets"]), "OLD_27_GITHUB_ASSETS_CHANGED")
    return release


class AnonymousRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, newurl):
        url = urllib.parse.urlsplit(newurl)
        require(url.scheme == "https" and url.hostname in {"github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"}
                and not url.username and not url.password, "PUBLIC_REDIRECT_OUTSIDE_GITHUB")
        return super().redirect_request(request, fp, code, message, headers, newurl)


def public_hash(expected):
    """匿名完整GET，遇可重试读取错误最多从头重读一次；不使用gh认证。"""
    url = "https://github.com/" + REPO + "/releases/download/" + TAG + "/" + urllib.parse.quote(expected["filename"], safe="")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), AnonymousRedirect())
    for attempt in range(2):
        value, count = hashlib.sha256(), 0
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "PartyOps-Windows-public-readback", "Accept": "application/octet-stream"})
            with opener.open(request, timeout=180) as response:
                require(response.status == 200, "ANONYMOUS_FULL_GET_STATUS_INVALID")
                for block in iter(lambda: response.read(1024 * 1024), b""):
                    count += len(block)
                    require(count <= expected["bytes"], "ANONYMOUS_ASSET_SIZE_EXCEEDED")
                    value.update(block)
            require(count == expected["bytes"] and value.hexdigest() == expected["sha256"], "ANONYMOUS_FULL_GET_SHA_MISMATCH")
            return {"filename": expected["filename"], "url": url, "bytes": count, "sha256": value.hexdigest(), "public_readback_verified": True}
        except urllib.error.HTTPError as error:
            if attempt or (error.code != 429 and not 500 <= error.code <= 599):
                raise RuntimeError("ANONYMOUS_FULL_GET_FAILED") from None
            time.sleep(20 if error.code == 429 else 2)
        except (TimeoutError, urllib.error.URLError, http.client.IncompleteRead):
            if attempt: raise RuntimeError("ANONYMOUS_FULL_GET_FAILED") from None
            time.sleep(2)


def find_asset(release, expected):
    matches = [row for row in release.get("assets", []) if row.get("name") == expected["filename"]]
    require(len(matches) <= 1, "DUPLICATE_GITHUB_ASSET_NAME")
    if not matches:
        return None
    row = matches[0]
    require(row.get("state") == "uploaded" and row.get("size") == expected["bytes"], "GITHUB_ASSET_SIZE_OR_STATE_CHANGED")
    if row.get("digest") is None:
        public_hash(expected)
    else:
        require(row["digest"] == "sha256:" + expected["sha256"], "GITHUB_ASSET_DIGEST_CHANGED")
    return row


def append(execute=False):
    require(not (HERE / "PUBLICATION_HOLD.json").exists(), "CANDIDATE_BLOCKED_BY_INDEPENDENT_REVIEW")
    require(execute is True, "EXPLICIT_GITHUB_ATTACHMENT_UPLOAD_REQUIRED")
    payload = checked()
    domestic_sha = domestic()
    release = observe(payload)
    for expected in payload["assets"]:
        find_asset(release, expected)  # 全部同名冲突在任何写之前拒绝。
    STATE.mkdir(exist_ok=True)
    binding = {"payload": payload, "domestic_readback_sha256": domestic_sha}
    intent = STATE / "upload.intent.json"
    if intent.exists():
        require(read(intent)["binding"] == binding, "GITHUB_UPLOAD_INTENT_CHANGED")
    else:
        exclusive(intent, {"binding": binding, "created_at": utc()})
    for expected in payload["assets"]:
        release = observe(payload)
        existing = find_asset(release, expected)
        if existing:
            continue
        intent = STATE / (expected["filename"] + ".upload-intent.json")
        require(not intent.exists(), "GITHUB_UPLOAD_OUTCOME_UNKNOWN_READ_ONLY_RECOVERY_REQUIRED")
        exclusive(intent, {"expected": expected, "payload_sha256": digest(EXPECTED), "created_at": utc()})
        # 单文件单次；失败/超时后只查读取回执，绝不自动重发或clobber。
        try:
            result = subprocess.run([str(GH), "release", "upload", TAG, "--repo", REPO, expected["path"]], capture_output=True, timeout=1800)
            command_ok = result.returncode == 0
            match = re.search(rb"HTTP (\d{3})", result.stderr)
            if not command_ok and match and int(match.group(1)) == 429: time.sleep(20)
            elif not command_ok: time.sleep(2)
        except subprocess.TimeoutExpired:
            command_ok = False; time.sleep(2)
        recovered = find_asset(observe(payload), expected)
        require(recovered is not None, "GITHUB_UPLOAD_OUTCOME_UNKNOWN_READ_ONLY_RECOVERY_REQUIRED")
        exclusive(STATE / (expected["filename"] + ".upload-result.json"), {"expected": expected, "asset": asset_identity(recovered),
                  "command_returned_success": command_ok, "outcome_read_verified": True})
    return {"status": "four-assets-present-awaiting-anonymous-readback", "old_27_preserved": True, "tag_unchanged": True}


def recover():
    payload = checked()
    domestic_sha = domestic()
    release = observe(payload)
    proofs, missing = [], []
    for expected in payload["assets"]:
        asset = find_asset(release, expected)
        if asset is None:
            missing.append(expected["filename"])
        else:
            proofs.append({"asset_id": asset["id"], **public_hash(expected)})
    final = observe(payload)
    for proof in proofs:
        expected = next(row for row in payload["assets"] if row["filename"] == proof["filename"])
        asset = find_asset(final, expected)
        require(asset is not None and asset["id"] == proof["asset_id"], "GITHUB_ASSET_CHANGED_DURING_READBACK")
    STATE.mkdir(exist_ok=True)
    receipt = {"status": "passed" if not missing else "partial", "repository": REPO, "tag": TAG, "release_id": RELEASE_ID,
               "package_sha256": SHA, "manifest_sha256": MANIFEST_SHA, "expected_sha256": digest(EXPECTED),
               "domestic_readback_sha256": domestic_sha, "assets": proofs, "missing": missing,
               "all_four_anonymous_full_get_verified": not missing and len(proofs) == 4,
               "old_27_preserved": True, "tag_unchanged": True, "network_write_performed": False, "observed_at": utc()}
    target = STATE / ("readback-" + uuid.uuid4().hex[:12] + ".json")
    exclusive(target, receipt)
    return {"status": receipt["status"], "readback": str(target), "missing": missing, "network_write_performed": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", nargs="?", choices=("prepare", "append", "recover"), default="prepare")
    parser.add_argument("--execute-network", action="store_true", help="仅append显式允许追加新附件")
    args = parser.parse_args()
    require(not args.execute_network or args.action == "append", "NETWORK_WRITE_FLAG_ONLY_FOR_APPEND")
    try:
        result = prepare() if args.action == "prepare" else append(args.execute_network) if args.action == "append" else recover()
        print(json.dumps(result, ensure_ascii=False))
        return 0 if result["status"] != "partial" else 3
    except Exception as error:
        code = str(error)
        print(json.dumps({"status": "failed", "error": code if isinstance(error, RuntimeError) and re.fullmatch(r"[A-Z][A-Z0-9_]+", code) else type(error).__name__}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())

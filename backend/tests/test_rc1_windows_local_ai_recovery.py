"""No untrusted or tampered historical local-AI binaries can enter a new build."""

import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location(
    "rc1_windows_recover_local_ai", ROOT / "scripts/rc1_windows_recover_local_ai.py"
)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def fixture(root: Path):
    root.mkdir(parents=True)
    entries = []
    for name in sorted(module.REQUIRED):
        blob = b"partyops b10331 test fixture" if name == "SOURCE.json" else name.encode() + b" test"
        (root / name).write_bytes(blob)
        entries.append({
            "path": name,
            "size": len(blob),
            "sha256": hashlib.sha256(blob).hexdigest(),
        })
    return entries


class OriginalLocalAIRecoveryContracts(unittest.TestCase):
    def test_required_original_runtime_verifies_by_size_and_sha(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "app"
            entries = fixture(root)
            result = module.validate_files(root, entries)
            self.assertEqual(set(result), module.REQUIRED)

    def test_byte_tamper_is_rejected_even_at_same_size(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "app"
            entries = fixture(root)
            changed = root / "llama.dll"
            original = changed.read_bytes()
            changed.write_bytes(original[:-1] + bytes([original[-1] ^ 1]))
            with self.assertRaisesRegex(ValueError, "hash/size mismatch"):
                module.validate_files(root, entries)

    def test_new_unrecorded_native_module_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "app"
            entries = fixture(root)
            (root / "ggml-new.dll").write_bytes(b"unknown")
            with self.assertRaisesRegex(ValueError, "hash/size mismatch"):
                module.validate_files(root, entries)

    def test_minimal_server_runtime_excludes_upstream_cli_and_bench_tools(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "app"
            entries = fixture(root)
            for name in ("llama-cli.exe", "llama-bench.exe", "ggml-rpc-server.exe"):
                blob = b"extra upstream tool"
                (root / name).write_bytes(blob)
                entries.append({
                    "path": name,
                    "size": len(blob),
                    "sha256": hashlib.sha256(blob).hexdigest(),
                })
            verified = module.validate_files(root, entries)
            self.assertTrue({"mtmd.dll", "libomp140.x86_64.dll"}.issubset(verified))
            self.assertFalse({"llama-cli.exe", "llama-bench.exe", "ggml-rpc-server.exe"} & set(verified))

    def test_missing_source_file_does_not_pass(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "app"
            entries = fixture(root)
            (root / "SOURCE.json").unlink()
            with self.assertRaisesRegex(ValueError, "input absent"):
                module.validate_files(root, entries)


if __name__ == "__main__":
    unittest.main()

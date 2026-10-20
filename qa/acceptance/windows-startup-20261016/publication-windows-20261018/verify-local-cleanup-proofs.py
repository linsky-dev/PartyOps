"""只读核验清理所需发布回执，避免 PowerShell 5.1 的 -c 参数引号丢失。"""
from pathlib import Path
import sys
import post_switch_common

if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("TWO_PROOF_PATHS_REQUIRED")
    post_switch_common.proofs(Path(sys.argv[1]), Path(sys.argv[2]))
    print("CURRENT_PRODUCTION_AND_GITHUB_PROOFS_VALID")

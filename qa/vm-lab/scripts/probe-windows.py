"""在实验室拥有的 Windows Guest 内采集 OS/ISA/启动标识证据。"""
from __future__ import annotations

import argparse
import base64
import json
import sys
from pathlib import Path

import paramiko

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from evidence import now, write_json
from lab import load_configuration
from providers import QemuLab


def encoded_powershell(script: str) -> str:
    script = "[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)\n" + script
    payload = base64.b64encode(script.encode("utf-16le")).decode("ascii")
    return f"powershell.exe -NoProfile -NonInteractive -EncodedCommand {payload}"


def execute(client: paramiko.SSHClient, script: str, timeout: int = 90) -> str:
    _stdin, stdout, stderr = client.exec_command(encoded_powershell(script), timeout=timeout)
    status = stdout.channel.recv_exit_status()
    output = stdout.read().decode("utf-8", errors="replace").strip()
    error = stderr.read().decode("utf-8", errors="replace").strip()
    if status != 0:
        raise RuntimeError(f"WINDOWS_GUEST_COMMAND_FAILED ({status}): {error[-2000:]}")
    return output


def probe(target: str) -> dict:
    matrix, media = load_configuration()
    spec = matrix["targets"].get(target)
    if not spec or spec["os"] != "windows":
        raise RuntimeError("WINDOWS_TARGET_REQUIRED")
    lab = QemuLab(matrix, Path(matrix["defaults"]["primary_root"]), media)
    state = lab.state(target)
    if not lab.live(state):
        raise RuntimeError("OWNED_WINDOWS_GUEST_NOT_RUNNING")

    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    try:
        client.connect(
            "127.0.0.1",
            port=spec["ssh_port"],
            username="partyopsqa",
            key_filename=str(lab.root / "keys" / "guest_ed25519"),
            allow_agent=False,
            look_for_keys=False,
            timeout=10,
            banner_timeout=10,
            auth_timeout=10,
        )
        output = execute(
            client,
            r"""
$ErrorActionPreference = 'Stop'
$identity = Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
$system = Get-CimInstance Win32_OperatingSystem
$computer = Get-CimInstance Win32_ComputerSystem
$arch = switch ($env:PROCESSOR_ARCHITECTURE) { 'AMD64' { 'x86_64' } 'x86' { 'i686' } 'ARM64' { 'arm64' } default { $env:PROCESSOR_ARCHITECTURE } }
$release = if ($system.Caption -match 'Windows 11') { '11' } elseif ($system.Caption -match 'Windows 10') { '10' } elseif ($system.Caption -match 'Windows 7') { '7 SP1' } else { $system.Caption }
[ordered]@{
  os = 'windows'
  os_release = $release
  arch = $arch
  boot_id = $system.LastBootUpTime.ToUniversalTime().ToString('o')
  distribution = 'Windows'
  caption = $system.Caption
  version = $system.Version
  build = $system.BuildNumber
  computer_name = $env:COMPUTERNAME
  timezone = (Get-TimeZone).Id
  hypervisor_present = [bool]$computer.HypervisorPresent
  identity = $identity
} | ConvertTo-Json -Compress -Depth 5
""",
        )
    finally:
        client.close()
    result = json.loads(output.lstrip("\ufeff"))
    identity = result.get("identity", {})
    if identity.get("uuid") != state["uuid"] or identity.get("purpose") != "disposable-qa":
        raise RuntimeError("WINDOWS_GUEST_UUID_MISMATCH")
    if result.get("os") != "windows" or result.get("arch") != spec["arch"]:
        raise RuntimeError("WINDOWS_GUEST_OS_ISA_MISMATCH")
    if result.get("os_release") != spec.get("os_release"):
        raise RuntimeError("WINDOWS_GUEST_OS_RELEASE_MISMATCH")
    if result.get("timezone") != "China Standard Time":
        raise RuntimeError("WINDOWS_GUEST_TIMEZONE_MISMATCH")
    result.update(generated_at=now(), isa_matches=True, owned_guest=True)
    destination = lab.vm_dir(target) / "guest-system.json"
    write_json(destination, result)
    write_json(lab.root / "state" / f"probe-{target}.json", {
        "generated_at": result["generated_at"], "target": target,
        "guest_uuid": state["uuid"], "evidence_path": str(destination),
        "os_release": result["os_release"], "arch": result["arch"],
        "boot_id": result["boot_id"], "timezone": result["timezone"],
    })
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("target")
    args = parser.parse_args()
    try:
        print(json.dumps(probe(args.target), ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError, RuntimeError, KeyError, json.JSONDecodeError) as exc:
        print(json.dumps({"status": "blocked", "error": str(exc)}, ensure_ascii=False, indent=2))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())

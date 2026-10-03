"""Windows OS/实际进程/安装包 ISA 合同；仿真、缺包和缺能力不能记为完整原生通过。"""
from __future__ import annotations

import base64
import json
import re
from pathlib import Path, PureWindowsPath

from evidence import safe_child, sha256, write_json

ISA = {"x86": "i686", "i686": "i686", "amd64": "x86_64", "x86_64": "x86_64", "aarch64": "arm64", "arm64": "arm64"}
PACKAGE_PROFILES = {"windows7_x86": ("i686", "legacy-core"), "windows7_amd64": ("x86_64", "legacy-smart"),
                    "windows_amd64": ("x86_64", "full")}
PROFILE_BLOCKERS = {"windows7_x86": "WINDOWS_LEGACY_CORE_OFFLINE_AI_NOT_BUNDLED",
                    "windows7_amd64": "WINDOWS_LEGACY_SMART_LOCAL_LLM_NOT_BUNDLED"}

# C# 使用 PowerShell 2/.NET 3.5 可编译语法；从 API 获取真实 OS，不依赖仿真环境变量。
MACHINE_PROBE = r'''
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class PartyOpsLabMachine {
  [StructLayout(LayoutKind.Sequential)] struct SYSTEM_INFO {
    public ushort arch, reserved; public uint pageSize; public IntPtr minimum, maximum;
    public UIntPtr mask; public uint processors, processorType, granularity; public ushort level, revision;
  }
  [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool IsWow64Process2(IntPtr handle, out ushort process, out ushort native);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool IsWow64Process(IntPtr handle, out bool wow);
  [DllImport("kernel32.dll")] static extern void GetNativeSystemInfo(out SYSTEM_INFO info);
  [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr handle, uint access, out IntPtr token);
  static string Name(ushort machine) {
    if (machine == 0x014c) return "i686"; if (machine == 0x8664) return "x86_64";
    if (machine == 0xaa64) return "arm64"; throw new Exception("WINDOWS_ISA_UNKNOWN");
  }
  public static string Query(uint pid) {
    IntPtr handle = OpenProcess(0x1000, false, pid);
    if (handle == IntPtr.Zero) throw new Exception("WINDOWS_PROCESS_QUERY_FAILED");
    try {
      ushort process, native;
      try {
        if (!IsWow64Process2(handle, out process, out native)) throw new Exception("WINDOWS_MACHINE_QUERY_FAILED");
        return Name(process == 0 ? native : process) + "|" + Name(native) + "|IsWow64Process2";
      } catch (EntryPointNotFoundException) {
        SYSTEM_INFO info; GetNativeSystemInfo(out info);
        if (info.arch != 0 && info.arch != 9) throw new Exception("WINDOWS_NATIVE_ISA_UNPROVEN");
        bool wow; if (!IsWow64Process(handle, out wow)) throw new Exception("WINDOWS_MACHINE_QUERY_FAILED");
        string os = info.arch == 0 ? "i686" : "x86_64";
        return (wow ? "i686" : os) + "|" + os + "|IsWow64Process+GetNativeSystemInfo";
      }
    } finally { CloseHandle(handle); }
  }
  public static string Token(uint pid) {
    IntPtr handle = OpenProcess(0x1000, false, pid), token = IntPtr.Zero;
    if (handle == IntPtr.Zero) throw new Exception("WINDOWS_PROCESS_QUERY_FAILED");
    try {
      if (!OpenProcessToken(handle, 8, out token)) throw new Exception("WINDOWS_PROCESS_TOKEN_QUERY_FAILED");
      using (WindowsIdentity identity = new WindowsIdentity(token)) {
        return identity.User.Value + "|" + new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator).ToString().ToLowerInvariant();
      }
    } finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(handle); }
  }
}
'@
$machineParts=([PartyOpsLabMachine]::Query([uint32]$PID)).Split('|')
$a=$machineParts[1]
'''


def validate_os_identity(system: dict, spec: dict) -> None:
    if spec.get("windows_execution_contract") != 1:
        return
    if system.get("os_isa") != spec["arch"] or system.get("arch") != system.get("os_isa"):
        raise RuntimeError("WINDOWS_NATIVE_OS_ISA_NOT_PROVEN")
    if system.get("isa_probe_api") != "IsWow64Process2" or not re.fullmatch(r"10\.0\.\d+", str(system.get("os_version", ""))):
        raise RuntimeError("WINDOWS_NATIVE_OS_API_OR_VERSION_NOT_PROVEN")
    if spec.get("requires_uefi_secureboot_tpm2"):
        if (system.get("secure_boot_enabled") is not True or system.get("tpm_spec_version") != "2.0"
                or system.get("installation_requirement_bypasses") is not False):
            raise RuntimeError("WINDOWS_ARM64_SECURE_BOOT_TPM2_NOT_PROVEN")


def execution_kind(os_isa: str, process_isa: str, version: str) -> str:
    if os_isa == process_isa and os_isa in {"i686", "x86_64", "arm64"}:
        return "native"
    if os_isa == "x86_64" and process_isa == "i686":
        return "wow64"
    match = re.fullmatch(r"10\.0\.(\d+)", version)
    if os_isa == "arm64" and match:
        if process_isa == "i686":
            return "windows-x86-emulation"
        if process_isa == "x86_64" and int(match[1]) >= 22000:
            return "windows-x64-emulation"
    raise RuntimeError("WINDOWS_APPLICATION_EXECUTION_UNSUPPORTED")


def validate_runtime(record: dict, spec: dict, package: dict, system: dict, environment: dict) -> None:
    validate_os_identity(system, spec)
    if not isinstance(record, dict) or type(record.get("schema_version")) is not int or record.get("schema_version") != 1 or record.get("probe_api") != "IsWow64Process2":
        raise RuntimeError("WINDOWS_RUNTIME_PROCESS_API_NOT_PROVEN")
    expected = PACKAGE_PROFILES.get(package.get("id"))
    if not expected or expected != (spec.get("package_arch"), spec.get("expected_runtime_profile")):
        raise RuntimeError("WINDOWS_TARGET_PACKAGE_LINE_MISMATCH")
    for field in ("process_isa", "pe_isa", "package_isa"):
        if record.get(field) != expected[0]:
            raise RuntimeError("WINDOWS_RUNTIME_ISA_MISMATCH:" + field)
    if record.get("runtime_profile") != expected[1]:
        raise RuntimeError("WINDOWS_RUNTIME_PROFILE_MISMATCH")
    if (record.get("package_platform") != package["id"].rsplit("_", 1)[0]
            or record.get("package_version") != package.get("version")):
        raise RuntimeError("WINDOWS_INSTALLED_PACKAGE_METADATA_MISMATCH")
    if (record.get("os_isa") != system["arch"] or record.get("os_version") != system.get("os_version")
            or record.get("boot_id") != system.get("boot_id")
            or str(record.get("guest_uuid", "")).casefold() != str(environment.get("vm_uuid", "")).casefold()):
        raise RuntimeError("WINDOWS_RUNTIME_GUEST_IDENTITY_MISMATCH")
    mode = execution_kind(record["os_isa"], record["process_isa"], record["os_version"])
    if record.get("execution_kind") != mode or spec.get("application_execution") != mode:
        raise RuntimeError("WINDOWS_EMULATION_MISREPORTED_AS_NATIVE")
    if record.get("installer_sha256") != package.get("sha256"):
        raise RuntimeError("WINDOWS_RUNTIME_INSTALLER_HASH_MISMATCH")
    for field in ("installed_executable_sha256", "installed_manifest_sha256"):
        if not re.fullmatch(r"[0-9a-f]{64}", str(record.get(field, ""))):
            raise RuntimeError("WINDOWS_INSTALLED_BINARY_BINDING_MISSING:" + field)
    if (record.get("manifest_executable_sha256") != record["installed_executable_sha256"]
            or record.get("manifest_executable_size") != record.get("installed_executable_size")
            or type(record.get("installed_executable_size")) is not int or record["installed_executable_size"] <= 0):
        raise RuntimeError("WINDOWS_INSTALLED_BINARY_MANIFEST_MISMATCH")
    if (type(record.get("pid")) is not int or record["pid"] <= 0 or record.get("is_admin") is not False
            or not re.fullmatch(r"S-1-5-21-(?:\d+-){3}\d+", str(record.get("owner_sid", "")))):
        raise RuntimeError("WINDOWS_STANDARD_USER_PROCESS_NOT_PROVEN")
    path = PureWindowsPath(str(record.get("executable_path", "")))
    if not path.is_absolute() or path.name.casefold() != "partyops.exe" or str(path).startswith("\\\\"):
        raise RuntimeError("WINDOWS_INSTALLED_PROCESS_PATH_INVALID")


def evidence_errors(result: dict, spec: dict, package: dict, directory: Path) -> list[str]:
    errors = []
    if package.get("id") in PROFILE_BLOCKERS:
        errors.append(PROFILE_BLOCKERS[package["id"]])
    if spec.get("windows_execution_contract") != 1:
        return errors
    try:
        entry = result.get("windows_runtime_evidence", {})
        path = safe_child(directory, directory / entry["path"])
        if sha256(path) != entry.get("sha256"):
            raise RuntimeError("WINDOWS_RUNTIME_EVIDENCE_HASH_MISMATCH")
        record = json.loads(path.read_text(encoding="utf-8-sig"))
        validate_runtime(record, spec, package, result["system"], result["environment"])
    except (RuntimeError, OSError, ValueError, TypeError, KeyError) as exc:
        errors.append(str(exc) if isinstance(exc, RuntimeError) else "WINDOWS_RUNTIME_EVIDENCE_MISSING_OR_INVALID")
    return errors


def runtime_script(pid: int, executable: str, installer: str) -> str:
    if type(pid) is not int or pid <= 0:
        raise ValueError("WINDOWS_RUNTIME_PID_INVALID")
    for value in (executable, installer):
        path = PureWindowsPath(value)
        if not path.is_absolute() or str(path).startswith("\\\\") or any(ord(c) < 32 for c in value):
            raise ValueError("WINDOWS_RUNTIME_LOCAL_PATH_REQUIRED")
    return r'''$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
''' + MACHINE_PROBE + r'''
function Hash($path) { $s=[IO.File]::OpenRead($path); try {$h=[Security.Cryptography.SHA256]::Create(); try {return ([BitConverter]::ToString($h.ComputeHash($s))).Replace('-','').ToLowerInvariant()}finally{$h.Dispose()}}finally{$s.Dispose()} }
$targetPid=__PID__
$expectedPath='__EXE__'
$installer='__INSTALLER__'
$p=Get-WmiObject Win32_Process -Filter ('ProcessId='+$targetPid)
if(-not $p -or -not [string]::Equals($p.ExecutablePath,$expectedPath,[StringComparison]::OrdinalIgnoreCase)) {throw 'WINDOWS_INSTALLED_PROCESS_PATH_MISMATCH'}
$runtime=([PartyOpsLabMachine]::Query([uint32]$targetPid)).Split('|')
$token=([PartyOpsLabMachine]::Token([uint32]$targetPid)).Split('|')
if($p.GetOwnerSid().Sid -ne $token[0]) {throw 'WINDOWS_PROCESS_OWNER_SID_MISMATCH'}
$stream=[IO.File]::OpenRead($expectedPath)
try { $reader=New-Object IO.BinaryReader($stream); if($reader.ReadUInt16() -ne 0x5a4d){throw 'WINDOWS_PE_INVALID'}; $stream.Position=60; $offset=$reader.ReadUInt32(); if($offset -lt 64 -or $offset -gt 1048576){throw 'WINDOWS_PE_INVALID'}; $stream.Position=$offset; if($reader.ReadUInt32() -ne 0x4550){throw 'WINDOWS_PE_INVALID'}; $peMachine=$reader.ReadUInt16() }finally{$stream.Dispose()}
$pe=if($peMachine -eq 0x014c){'i686'}elseif($peMachine -eq 0x8664){'x86_64'}elseif($peMachine -eq 0xaa64){'arm64'}else{throw 'WINDOWS_PE_MACHINE_UNSUPPORTED'}
$manifestPath=Join-Path ([IO.Path]::GetDirectoryName($expectedPath)) 'release-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$entries=@($manifest.files | Where-Object {$_.path -ieq 'PartyOps.exe'})
if($manifest.product -ne 'PartyOps' -or $manifest.schema_version -ne 1 -or $entries.Count -ne 1){throw 'WINDOWS_MANIFEST_EXE_MISSING'}
$packageIsa=@{'x86'='i686';'amd64'='x86_64';'arm64'='arm64'}[[string]$manifest.architecture]
$s=Get-WmiObject Win32_OperatingSystem
$marker=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
if($marker.purpose -ne 'disposable-qa'){throw 'WINDOWS_GUEST_OWNERSHIP_MISMATCH'}
$kind=if($runtime[0] -eq $runtime[1]){'native'}elseif($runtime[1] -eq 'x86_64' -and $runtime[0] -eq 'i686'){'wow64'}elseif($runtime[1] -eq 'arm64' -and $runtime[0] -eq 'i686'){'windows-x86-emulation'}elseif($runtime[1] -eq 'arm64' -and $runtime[0] -eq 'x86_64'){'windows-x64-emulation'}else{'unsupported'}
$after=Get-WmiObject Win32_Process -Filter ('ProcessId='+$targetPid)
if(-not $after -or $after.CreationDate -ne $p.CreationDate -or $after.ExecutablePath -ne $p.ExecutablePath){throw 'WINDOWS_RUNTIME_PROCESS_CHANGED'}
[ordered]@{schema_version=1;pid=[int]$targetPid;owner_sid=$token[0];is_admin=($token[1] -eq 'true');executable_path=$p.ExecutablePath;process_created_at=$p.CreationDate;guest_uuid=$marker.uuid;boot_id=$s.LastBootUpTime;os_version=$s.Version;os_isa=$runtime[1];process_isa=$runtime[0];probe_api=$runtime[2];pe_isa=$pe;package_isa=$packageIsa;execution_kind=$kind;runtime_profile=$manifest.runtime_profile;package_platform=$manifest.platform;package_version=$manifest.version;installer_sha256=(Hash $installer);installed_executable_sha256=(Hash $expectedPath);installed_manifest_sha256=(Hash $manifestPath);installed_executable_size=([IO.FileInfo]$expectedPath).Length;manifest_executable_sha256=$entries[0].sha256;manifest_executable_size=$entries[0].size} | ConvertTo-Json -Compress
'''.replace("__PID__", str(pid)).replace("__EXE__", executable.replace("'", "''")).replace("__INSTALLER__", installer.replace("'", "''"))


def capture(lab, target: str, package: dict, pid: int, executable: str, directory: Path) -> dict:
    """只观测已存在普通用户进程；不启动应用、Guest、账户或业务修改。"""
    from identity import probe, runtime_binding
    spec = lab.matrix["targets"][target]
    if spec.get("windows_execution_contract") != 1 or spec.get("backend") != "qemu":
        raise RuntimeError("WINDOWS_EXECUTION_CONTRACT_TARGET_REQUIRED")
    directory = safe_child(lab.root / "reports" / target, directory)
    system, environment = probe(lab, target), runtime_binding(lab, target)
    installer = "C:\\PartyOps-QA\\incoming\\" + Path(package["path"]).name
    command = "powershell.exe -NoProfile -EncodedCommand " + base64.b64encode(runtime_script(pid, executable, installer).encode("utf-16le")).decode()
    record = json.loads(lab.ssh(target, command, timeout=60).strip())
    validate_runtime(record, spec, package, system, environment)
    path = directory / "windows-runtime.json"
    write_json(path, record)
    return {"path": path.name, "sha256": sha256(path), "runtime_environment_passed": False}

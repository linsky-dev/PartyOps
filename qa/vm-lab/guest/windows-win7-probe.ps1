param([Parameter(Mandatory=$true)][string]$ContextPath)
# 只由已登记 Guest 的交互式有限权限任务调用；PS2/.NET 兼容，不加载开发源码。
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
[void][Reflection.Assembly]::LoadWithPartialName('System.Web.Extensions')
$json = New-Object Web.Script.Serialization.JavaScriptSerializer

function Start-ProbeCapturedProcess([string]$Exe, [string]$Arguments, [string]$Directory, [string]$Stdout, [string]$Stderr) {
    # PS2的WindowStyle与重定向属于互斥参数集；直接复用.NET Process隐藏启动并异步排空两路日志。
    if (-not ('PartyOpsLabCapturedProcess' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
public static class PartyOpsLabCapturedProcess {
    public static Process Start(string exe, string arguments, string directory, string stdout, string stderr) {
        Process process = new Process();
        process.StartInfo = new ProcessStartInfo(exe, arguments);
        process.StartInfo.WorkingDirectory = directory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        StreamWriter output = new StreamWriter(stdout, false, new UTF8Encoding(false));
        StreamWriter error = new StreamWriter(stderr, false, new UTF8Encoding(false));
        output.AutoFlush = true; error.AutoFlush = true;
        process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data == null) output.Close(); else output.WriteLine(e.Data); };
        process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data == null) error.Close(); else error.WriteLine(e.Data); };
        try { process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process; }
        catch { output.Close(); error.Close(); process.Dispose(); throw; }
    }
}
'@
    }
    return [PartyOpsLabCapturedProcess]::Start($Exe, $Arguments, $Directory, $Stdout, $Stderr)
}

function Get-ProbeHash([string]$Path) {
    $ancestor = $Path
    while ($ancestor) {
        if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'PROBE_REPARSE_POINT_REJECTED' }
        $ancestor = Split-Path -Path $ancestor -Parent
    }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    # CLR 2 的 HashAlgorithm 通过 Clear 释放；没有可直接调用的公开 Dispose。
    finally { $stream.Dispose(); $algorithm.Clear() }
}

function Get-ProbeProcess($Process, [string]$ExpectedSid, [string]$ExpectedPath, [int]$SessionId) {
    $rows = @(Get-WmiObject Win32_Process -Filter ('ProcessId=' + $Process.Id))
    if ($rows.Count -ne 1) { throw 'STANDARD_CHILD_PROCESS_NOT_OBSERVED' }
    $row = $rows[0]; $owner = $row.GetOwnerSid()
    if ($owner.ReturnValue -ne 0 -or $owner.Sid -ne $ExpectedSid -or
        $row.ExecutablePath -ne $ExpectedPath -or $row.SessionId -ne $SessionId -or
        $row.ParentProcessId -ne $PID) { throw 'STANDARD_CHILD_IDENTITY_MISMATCH' }
    return @{pid=[int]$row.ProcessId; parent_pid=[int]$row.ParentProcessId; sid=[string]$owner.Sid;
        session_id=[int]$row.SessionId; path=[string]$row.ExecutablePath; created=[string]$row.CreationDate}
}

function Get-ProbeNetstat {
    $rows = @(& (Join-Path $env:SystemRoot 'System32\netstat.exe') -ano -p tcp)
    if ($LASTEXITCODE -ne 0) { throw 'STANDARD_NETSTAT_FAILED' }
    return $rows
}

function Test-ProbeListener([int]$Port, [int]$ExpectedPid) {
    $rows = @(Get-ProbeNetstat)
    $owners = @($rows | ForEach-Object {
        if ($_ -match ('^\s*TCP\s+127\.0\.0\.1:' + $Port + '\s+\S+\s+LISTENING\s+(\d+)\s*$')) { [int]$matches[1] }
    })
    if ($owners.Count -eq 0) { return $false }
    if ($owners.Count -ne 1 -or $owners[0] -ne $ExpectedPid) { throw 'STANDARD_LISTENER_OWNER_MISMATCH' }
    return $true
}

function Read-ProbeHttp([string]$Url) {
    $request = [Net.HttpWebRequest]::Create($Url)
    $request.Proxy = $null; $request.Timeout = 5000; $request.ReadWriteTimeout = 5000
    $response = $request.GetResponse()
    try {
        $reader = New-Object IO.StreamReader($response.GetResponseStream(), [Text.Encoding]::UTF8)
        try { return $reader.ReadToEnd() } finally { $reader.Close() }
    } finally { $response.Close() }
}

function Assert-ProbeToken($Context) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $groups = @($identity.Groups | ForEach-Object { $_.Value })
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    if ($identity.User.Value -ne $Context['sid'] -or $session -eq 0 -or $session -ne $Context['session_id'] -or
        $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $groups -contains 'S-1-5-32-544') {
        throw 'STANDARD_INTERACTIVE_TOKEN_REQUIRED'
    }
    $knownLocal = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $knownApp = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    $profile = Get-ItemProperty -LiteralPath ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\' + $identity.User.Value)
    if ($knownLocal -ne $env:LOCALAPPDATA -or $knownApp -ne $env:APPDATA -or
        [Environment]::ExpandEnvironmentVariables([string]$profile.ProfileImagePath) -ne $env:USERPROFILE -or
        $knownLocal -ne (Join-Path $env:USERPROFILE 'AppData\Local')) { throw 'STANDARD_PROFILE_ENVIRONMENT_MISMATCH' }
    return @{sid=[string]$identity.User.Value; administrator=$false; session_id=[int]$session;
        userprofile=[string]$env:USERPROFILE; localappdata=[string]$knownLocal; appdata=[string]$knownApp}
}

function Invoke-Win7InstalledProbe($Context, $Result) {
    # PS2函数输出是PSObject包装；嵌入报告前解除包装，避免旧JSON序列化器循环引用。
    $Result['token'] = [hashtable](Assert-ProbeToken $Context)
    $marker = $json.DeserializeObject([IO.File]::ReadAllText('C:\ProgramData\PartyOps-VM-Lab\identity.json'))
    $os = Get-WmiObject Win32_OperatingSystem
    $hardware = [string](Get-WmiObject Win32_ComputerSystemProduct).UUID
    if ($hardware -ne $Context['uuid'] -or $marker['uuid'] -ne $Context['uuid'] -or $marker['purpose'] -ne 'disposable-qa' -or
        $os.LastBootUpTime -ne $Context['boot_id'] -or $os.Version -ne '6.1.7601' -or $os.ServicePackMajorVersion -ne 1) { throw 'STANDARD_GUEST_IDENTITY_MISMATCH' }
    $exe = [string]$Context['app_path']
    if ((Get-ProbeHash $exe) -ne $Context['app_sha256'] -or
        (Get-ProbeHash (Join-Path (Split-Path $exe -Parent) 'release-manifest.json')) -ne $Context['manifest_sha256']) { throw 'STANDARD_INSTALLED_PAYLOAD_CHANGED' }
    $output = Join-Path (Split-Path $ContextPath -Parent) 'output'
    $data = Join-Path $env:LOCALAPPDATA ('PartyOps-QA\' + $Context['run_id'] + '\中文 空格启动数据')
    if (Test-Path -LiteralPath $data) { throw 'STANDARD_PROBE_DATA_ALREADY_EXISTS' }
    [void][IO.Directory]::CreateDirectory($data)
    $Result['data_path'] = [string]$data
    $permission = Start-ProbeCapturedProcess $exe '--startup-user-permission-self-test' (Split-Path $exe -Parent) (Join-Path $output 'permission.stdout') (Join-Path $output 'permission.stderr')
    $handle = $permission.Handle
    $Result['permission_started_pid'] = [int]$permission.Id
    $Result['permission_process'] = [hashtable](Get-ProbeProcess $permission $Context['sid'] $exe $Context['session_id'])
    if (-not $permission.WaitForExit(120000)) { throw 'STANDARD_PERMISSION_TIMEOUT_CHECK_PROCESS_BEFORE_RETRY' }
    $permission.WaitForExit() # 等待异步日志排空，防止JSON最后一行尚未写入。
    $permission.Refresh(); $Result['permission_exit_code'] = [int]$permission.ExitCode
    if ($null -eq $permission.ExitCode) { throw 'STANDARD_PERMISSION_EXIT_CODE_MISSING' }
    $Result['permission'] = $json.DeserializeObject([IO.File]::ReadAllText((Join-Path $output 'permission.stdout'), [Text.Encoding]::UTF8))
    if ($permission.ExitCode -ne 0 -or $Result['permission']['passed'] -ne $true -or
        $Result['permission']['runtime_readable'] -ne $true -or $Result['permission']['user_temp_writable'] -ne $true) { throw 'STANDARD_PERMISSION_PREFLIGHT_FAILED' }
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
    $env:PARTYOPS_DATA_DIR = $data; $env:PARTYOPS_MODE = 'personal'; $env:PARTYOPS_ENVIRONMENT = 'production'
    $env:PARTYOPS_HOST = '127.0.0.1'; $env:PARTYOPS_BIND_HOST = '127.0.0.1'; $env:PARTYOPS_ADVERTISE_HOST = '127.0.0.1'
    $env:PARTYOPS_PORT = [string]$port; $env:PARTYOPS_AGENT_PORT = [string]$port; $env:PARTYOPS_LOCAL_AI_PORT = [string]$port
    $env:PARTYOPS_TLS_ENABLED = 'false'; $env:PYTHONUTF8 = '1'
    $server = Start-ProbeCapturedProcess $exe '--startup-self-test-child' (Split-Path $exe -Parent) (Join-Path $output 'server.stdout') (Join-Path $output 'server.stderr')
    $serverHandle = $server.Handle
    $Result['server_started_pid'] = [int]$server.Id
    try {
        $Result['server_process'] = [hashtable](Get-ProbeProcess $server $Context['sid'] $exe $Context['session_id'])
        $deadline = [DateTime]::UtcNow.AddSeconds(180)
        $lastFailure = 'STANDARD_HEALTH_NOT_READY'
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($server.HasExited) { $server.Refresh(); $Result['server_exit_code'] = [int]$server.ExitCode; throw 'STANDARD_SERVER_EARLY_EXIT' }
            if (Test-ProbeListener $port $server.Id) {
                try {
                    $raw = Read-ProbeHttp ('http://127.0.0.1:' + $port + '/api/v1/health')
                    $health = $json.DeserializeObject($raw)
                    if ($health['status'] -ne 'ok' -or $health['app_version'] -ne $Context['version'] -or $health['mode'] -ne 'personal' -or
                        $health['sqlite']['safe_version'] -ne $true -or $health['sqlite']['fts5'] -ne $true) { throw 'STANDARD_HEALTH_CONTRACT_FAILED' }
                    $frontend = Read-ProbeHttp ('http://127.0.0.1:' + $port + '/')
                    if ($frontend -notmatch '(?i)<!doctype html' -or $frontend -notmatch 'id="app"') { throw 'STANDARD_FRONTEND_MISSING' }
                    $Result['health'] = $health; $Result['health_port'] = [int]$port
                    $Result['listener_pid'] = [int]$server.Id; $Result['frontend_ready'] = $true
                    $Result['server_process_after_health'] = [hashtable](Get-ProbeProcess $server $Context['sid'] $exe $Context['session_id'])
                    $Result['app_sha256_after'] = [string](Get-ProbeHash $exe)
                    if ($Result['app_sha256_after'] -ne $Context['app_sha256']) { throw 'STANDARD_INSTALLED_PAYLOAD_CHANGED' }
                    $Result['exit_code'] = 0
                    return
                } catch { $lastFailure = $_.Exception.Message }
            }
            Start-Sleep -Milliseconds 500
        }
        throw ('STANDARD_HEALTH_TIMEOUT:' + $lastFailure)
    } finally {
        # 只停止本次由句柄创建的临时诊断进程；明确记录非优雅退出，不代表用户重启或卸载。
        if (-not $server.HasExited) {
            $server.Kill(); $Result['diagnostic_stop'] = 'owned-handle-force-stop'
            if (-not $server.WaitForExit(10000)) { throw 'STANDARD_DIAGNOSTIC_PROCESS_STILL_RUNNING' }
        } else { $Result['diagnostic_stop'] = 'already-exited' }
        $server.WaitForExit()
        $server.Refresh(); $Result['server_exit_code'] = [int]$server.ExitCode
    }
}

$context = $json.DeserializeObject([IO.File]::ReadAllText($ContextPath, [Text.Encoding]::UTF8))
$resultPath = Join-Path (Split-Path $ContextPath -Parent) 'output\result.json'
if (Test-Path -LiteralPath $resultPath) { throw 'STANDARD_RESULT_ALREADY_EXISTS' }
$result = @{schema_version=1; run_id=[string]$context['run_id']; context_sha256=[string](Get-ProbeHash $ContextPath);
    started_at=[DateTime]::UtcNow.ToString('o'); exit_code=1; runtime_environment_passed=$false}
try { Invoke-Win7InstalledProbe $context $result }
catch { $result['error']=[string]$_.Exception.Message; $result['error_location']=[string]$_.InvocationInfo.PositionMessage; $result['exit_code']=1 }
$result['finished_at']=[DateTime]::UtcNow.ToString('o')
[IO.File]::WriteAllText($resultPath, $json.Serialize($result), (New-Object Text.UTF8Encoding($false)))
exit ([int]$result['exit_code'])

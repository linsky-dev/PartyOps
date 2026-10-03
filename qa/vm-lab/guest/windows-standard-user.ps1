param(
    [Parameter(Mandatory = $true)][string]$ExpectedUuid,
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Launch', 'Inspect')][string]$Action,
    [string]$InstallDir = 'C:\PartyOps QA\中文 程序'
)
# Windows 7 PowerShell 2 与 Windows 10/11 共用内置 ADSI、WMI 和任务计划程序。
# 只在已登记的一次性 Guest 中操作，凭据仅留在该 Guest 的受限目录。
$ErrorActionPreference = 'Stop'

function New-LabJsonSerializer {
    $assembly = [Reflection.Assembly]::LoadWithPartialName('System.Web.Extensions')
    if (-not $assembly) { throw 'GUEST_JSON_RUNTIME_MISSING' }
    return New-Object System.Web.Script.Serialization.JavaScriptSerializer
}

function Read-LabJson([string]$Path) {
    $serializer = New-LabJsonSerializer
    return $serializer.DeserializeObject([IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8))
}

function Write-LabPrivateJson([string]$Path, $Value) {
    $serializer = New-LabJsonSerializer
    $encoding = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $serializer.Serialize($Value), $encoding)
}

function Get-LabLocalUser([string]$Name) {
    $computer = [ADSI]('WinNT://' + $env:COMPUTERNAME + ',computer')
    return @($computer.Children | Where-Object { $_.SchemaClassName -eq 'User' -and $_.Name -eq $Name })
}

function Get-LabLocalGroup([string]$Sid) {
    $groups = @(Get-WmiObject Win32_Group -Filter ("LocalAccount=True AND SID='" + $Sid + "'"))
    if ($groups.Count -ne 1) { throw 'GUEST_LOCAL_GROUP_IDENTITY_INVALID' }
    return [ADSI]('WinNT://' + $env:COMPUTERNAME + '/' + $groups[0].Name + ',group')
}

function New-LabLocalUser([string]$Name, [string]$Password) {
    $computer = [ADSI]('WinNT://' + $env:COMPUTERNAME + ',computer')
    $user = $computer.Create('user', $Name)
    $user.SetPassword($Password)
    $user.Put('Description', 'PartyOps isolated standard-user acceptance')
    # NORMAL_ACCOUNT 与 DONT_EXPIRE_PASSWD；不授予管理员权限。
    $user.Put('UserFlags', 0x10200)
    $user.SetInfo()
    $users = Get-LabLocalGroup 'S-1-5-32-545'
    $users.Add('WinNT://' + $env:COMPUTERNAME + '/' + $Name + ',user')
}

function Get-LabUserSid($User) {
    $bytes = [byte[]]$User.Properties['objectSid'].Value
    return (New-Object Security.Principal.SecurityIdentifier($bytes, 0)).Value
}

function Get-LabAdministratorMemberSids {
    $administrators = Get-LabLocalGroup 'S-1-5-32-544'
    foreach ($member in @($administrators.Invoke('Members'))) {
        $bytes = [byte[]]$member.GetType().InvokeMember('ObjectSID', 'GetProperty', $null, $member, $null)
        (New-Object Security.Principal.SecurityIdentifier($bytes, 0)).Value
    }
}

function Start-LabInteractiveTask([string]$Launcher, [string]$Directory, [string]$Name) {
    $scheduler = New-Object -ComObject 'Schedule.Service'
    $scheduler.Connect()
    $definition = $scheduler.NewTask(0)
    $definition.RegistrationInfo.Description = 'PartyOps isolated standard-user acceptance'
    $definition.Principal.UserId = $env:COMPUTERNAME + '\' + $Name
    $definition.Principal.LogonType = 3 # TASK_LOGON_INTERACTIVE_TOKEN，无密码任务。
    $definition.Principal.RunLevel = 0 # TASK_RUNLEVEL_LUA，普通用户权限。
    $definition.Settings.Enabled = $true
    $definition.Settings.AllowDemandStart = $true
    $definition.Settings.MultipleInstances = 2
    $taskAction = $definition.Actions.Create(0)
    $taskAction.Path = $Launcher
    $taskAction.WorkingDirectory = $Directory
    $folder = $scheduler.GetFolder('\')
    $registered = $folder.RegisterTaskDefinition('PartyOps-QA-Standard-Launch', $definition, 6,
        $definition.Principal.UserId, $null, 3, $null)
    $registered.Run($null) | Out-Null
}

function Invoke-StandardUserAcceptance([string]$ExpectedUuid, [string]$Action, [string]$InstallDir) {
    $root = 'C:\ProgramData\PartyOps-VM-Lab'
    $marker = Read-LabJson (Join-Path $root 'identity.json')
    $hardware = [string](Get-WmiObject Win32_ComputerSystemProduct).UUID
    if ($hardware -ne $ExpectedUuid -or $marker['uuid'] -ne $ExpectedUuid -or $marker['purpose'] -ne 'disposable-qa') {
        throw 'GUEST_OWNERSHIP_MISMATCH'
    }
    $name = 'partyopsuser'
    $private = Join-Path $root 'private'
    $credentialPath = Join-Path $private 'standard-user.local.json'
    $existing = @(Get-LabLocalUser $name)
    if ($existing.Count -gt 1) { throw 'STANDARD_USER_IDENTITY_AMBIGUOUS' }
    if ($existing.Count -eq 1 -and -not (Test-Path -LiteralPath $credentialPath)) { throw 'STANDARD_USER_NOT_LAB_OWNED' }
    if ($Action -eq 'Prepare') {
        if (-not (Test-Path -LiteralPath (Join-Path $root 'ready.json'))) { throw 'GUEST_BOOTSTRAP_NOT_READY' }
        if ($existing.Count -eq 0) {
            # 如前次只完成凭据写入，复用同一 Guest 的凭据恢复创建；不改写他人同名账户。
            if (Test-Path -LiteralPath $credentialPath) {
                $pending = Read-LabJson $credentialPath
                if ($pending['uuid'] -ne $ExpectedUuid -or $pending['username'] -ne $name -or -not $pending['password']) {
                    throw 'STANDARD_USER_CREDENTIAL_MISMATCH'
                }
                $password = $pending['password']
            } else {
                New-Item -ItemType Directory -Path $private -Force | Out-Null
                & icacls.exe $private /inheritance:r /grant '*S-1-5-18:(OI)(CI)F' /grant '*S-1-5-32-544:(OI)(CI)F' | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'GUEST_PRIVATE_ACL_FAILED' }
                $password = 'Pq8!' + [guid]::NewGuid().ToString('N')
                Write-LabPrivateJson $credentialPath @{uuid=$ExpectedUuid; username=$name; password=$password}
            }
            New-LabLocalUser $name $password
        }
    }
    if (-not (Test-Path -LiteralPath $credentialPath)) { throw 'STANDARD_USER_NOT_PREPARED' }
    $credential = Read-LabJson $credentialPath
    if ($credential['uuid'] -ne $ExpectedUuid -or $credential['username'] -ne $name -or -not $credential['password']) {
        throw 'STANDARD_USER_CREDENTIAL_MISMATCH'
    }
    $users = @(Get-LabLocalUser $name)
    if ($users.Count -ne 1) { throw 'STANDARD_USER_NOT_PREPARED' }
    $sid = [string](Get-LabUserSid $users[0])
    if (@(Get-LabAdministratorMemberSids) -contains $sid) { throw 'STANDARD_USER_IS_ADMINISTRATOR' }
    if ($Action -eq 'Prepare') {
        # 核验普通账户后才设置自动登录，秘密不进入输出或宿主文件。
        $logon = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
        foreach ($entry in @{AutoAdminLogon='1'; DefaultUserName=$name; DefaultPassword=$credential['password']; DefaultDomainName=$env:COMPUTERNAME}.GetEnumerator()) {
            Set-ItemProperty -Path $logon -Name $entry.Key -Value $entry.Value
        }
        New-ItemProperty -Path $logon -Name 'AutoLogonCount' -Value 99 -PropertyType DWord -Force | Out-Null
    }
    [hashtable[]]$desktop = @(Get-WmiObject Win32_Process -Filter "Name='explorer.exe'" | ForEach-Object {
        $owner = $_.GetOwner()
        if ($owner.ReturnValue -eq 0 -and $owner.User -eq $name -and $owner.Domain -eq $env:COMPUTERNAME) {
            @{pid=[int]$_.ProcessId; session_id=[int]$_.SessionId; owner=[string]$owner.User}
        }
    })
    if ($Action -eq 'Launch') {
        if ($desktop.Count -ne 1 -or $desktop[0].session_id -eq 0) { throw 'STANDARD_USER_INTERACTIVE_DESKTOP_REQUIRED' }
        $launcher = Join-Path $InstallDir 'PartyOpsLauncher.exe'
        if (-not (Test-Path -LiteralPath $launcher)) { throw 'INSTALLED_LAUNCHER_MISSING' }
        # COM任务创建的附带管道输出不能混入唯一的JSON报告。
        Start-LabInteractiveTask $launcher $InstallDir $name | Out-Null
    }
    [hashtable[]]$processes = @(Get-WmiObject Win32_Process -Filter "Name LIKE 'PartyOps%'" | ForEach-Object {
        $owner = $_.GetOwner()
        @{name=[string]$_.Name; pid=[int]$_.ProcessId; session_id=[int]$_.SessionId; owner=[string]$owner.User; path=[string]$_.ExecutablePath}
    })
    $profiles = @(Get-WmiObject Win32_UserProfile -Filter ("SID='" + $sid + "'"))
    if ($profiles.Count -gt 1) { throw 'STANDARD_USER_PROFILE_AMBIGUOUS' }
    # 使用 .NET 原始字符串，避免 provider/Get-Date 扩展属性使旧 JSON 序列化器递归 PSObject。
    $config = if ($profiles.Count -eq 1) { [IO.Path]::Combine([string]$profiles[0].LocalPath, 'AppData\Local\PartyOps') } else { $null }
    $wizard = if ($config -and (Test-Path -LiteralPath (Join-Path $config 'wizard.url'))) {
        [IO.File]::ReadAllText((Join-Path $config 'wizard.url'), [Text.Encoding]::UTF8)
    } else { $null }
    return @{
        # PS2的WMI标量携带PSObject属性；报告只接受明确的.NET基础值。
        generated_at=[DateTime]::Now.ToString('o'); uuid=[string]$hardware; action=[string]$Action; username=[string]$name; sid=[string]$sid
        administrator=$false; desktop=$desktop; processes=$processes; config_dir=$config; wizard_url=$wizard
        runtime_environment_passed=$false
    }
}

$result = Invoke-StandardUserAcceptance $ExpectedUuid $Action $InstallDir
$serializer = New-LabJsonSerializer
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
[Console]::WriteLine($serializer.Serialize($result))

param(
  [string]$LabRoot = 'D:\PartyOps-VM-Lab',
  [switch]$EnableWhpx
)
$ErrorActionPreference = 'Stop'
$labPath = [IO.Path]::GetFullPath($LabRoot)
if ($labPath -eq [IO.Path]::GetPathRoot($labPath)) { throw '实验室目录不能是盘根。' }
$downloads = Join-Path $labPath 'downloads'
$qemuHome = Join-Path $labPath 'tools\qemu-11.1.0'
$records = Join-Path $labPath 'state'
New-Item -ItemType Directory -Path $downloads,$records -Force | Out-Null
$installer = Join-Path $downloads 'qemu-w64-setup-20260811.exe'
$expected = '5bcf9eed634e8575a37b74f445af41a2fe4106da512d0c30c368301d4c105037fdfab40a5287367a28a957624cddebbc8c07e16c88ab6634f554cdf3d16bf543'
$url = 'https://qemu.weilnetz.de/w64/qemu-w64-setup-20260811.exe'
if (-not (Test-Path -LiteralPath $installer)) {
  Write-Host '下载官方 QEMU 下载页所列 Windows 发行商的 11.1.0 安装器。'
  & curl.exe --fail --location --connect-timeout 30 --max-time 1800 --output "$installer.part" $url
  if ($LASTEXITCODE -ne 0) { throw 'QEMU 下载失败，保留 .part 供诊断，未执行安装器。' }
  $actual = (Get-FileHash -LiteralPath "$installer.part" -Algorithm SHA512).Hash.ToLowerInvariant()
  if ($actual -ne $expected) { throw 'QEMU SHA-512 与发布方值不符，拒绝安装。' }
  Move-Item -LiteralPath "$installer.part" -Destination $installer
}
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA512).Hash.ToLowerInvariant() -ne $expected) {
  throw '缓存安装器校验失败，拒绝执行。'
}
$qemuExe = Join-Path $qemuHome 'qemu-system-x86_64.exe'
if (-not (Test-Path -LiteralPath $qemuExe)) {
  $installProcess = Start-Process -FilePath $installer -ArgumentList @('/S',"/D=$qemuHome") -WindowStyle Hidden -Wait -PassThru
  if ($installProcess.ExitCode -ne 0) { throw "QEMU 安装失败：$($installProcess.ExitCode)" }
}
$versions = @{}
foreach ($entry in @('qemu-system-x86_64','qemu-system-i386','qemu-system-aarch64','qemu-system-loongarch64','qemu-img')) {
  $binary = Join-Path $qemuHome "$entry.exe"
  if (-not (Test-Path -LiteralPath $binary)) { throw "缺少 $entry" }
  $version = & $binary --version
  if ($LASTEXITCODE -ne 0) { throw "$entry 无法启动" }
  $versions[$entry] = @($version)[0]
}
$loongFirmware=@{}
foreach($name in @('edk2-loongarch64-code.fd','edk2-loongarch64-vars.fd')) {
  $firmware=Join-Path $qemuHome "share\$name"
  if(-not (Test-Path -LiteralPath $firmware)){throw "缺少 LoongArch UEFI：$name"}
  $loongFirmware[$name]=@{path=$firmware;sha256=(Get-FileHash -LiteralPath $firmware -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$restartNeeded = $false
if ($EnableWhpx) {
  $feature = Get-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform
  if ($feature.State -ne 'Enabled') {
    $change = Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform -All -NoRestart
    $restartNeeded = [bool]$change.RestartNeeded
  }
}
$record = [ordered]@{
  generated_at = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o')
  source_url = $url
  checksum_url = 'https://qemu.weilnetz.de/w64/qemu-w64-setup-20260811.sha512'
  sha512 = $expected
  sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
  authenticode_status = (Get-AuthenticodeSignature -LiteralPath $installer).Status.ToString()
  qemu_home = $qemuHome
  versions = $versions
  loongarch_uefi = $loongFirmware
  restart_required = $restartNeeded
  security_features_disabled = @()
}
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $records 'bootstrap-windows.json') -Encoding utf8NoBOM
$record | ConvertTo-Json -Depth 6
if ($restartNeeded) { Write-Warning 'WHPX 已启用但宿主机需要重启；没有自动重启，后续允许明确使用 TCG。' }

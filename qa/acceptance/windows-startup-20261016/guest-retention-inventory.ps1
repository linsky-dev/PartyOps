$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$identity=Get-Content -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\identity.json' -Raw | ConvertFrom-Json
if($identity.uuid -ne 'f47cf265-72a2-4d51-9cf1-b1f5ae960cb2' -or $identity.purpose -ne 'disposable-qa'){throw 'QA_GUEST_IDENTITY_MISMATCH'}
# 只输出既有合成验收的路径和记录存在性；不输出登录口令或会话。
$records=@(Get-ChildItem -LiteralPath 'C:\ProgramData\PartyOps-VM-Lab\private' -Filter 'lifecycle-*.json' -File -ErrorAction SilentlyContinue | ForEach-Object {
  $value=Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
  @{record=$_.Name;data_dir=$value.data_dir;has_task=[bool]$value.task_id;has_attachment=[bool]$value.attachment_id;business_verified=$value.business_verified;last_write=$_.LastWriteTimeUtc.ToString('o')}
})
$sessions=@(Get-Process -Name 'PartyOps' -ErrorAction SilentlyContinue | Select-Object Id,SessionId)
@{records=$records;processes=$sessions} | ConvertTo-Json -Depth 4 -Compress

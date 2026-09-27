param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [ValidateSet('AnyCPU','x86','x64')][string]$Platform = 'AnyCPU'
)
& "$PSScriptRoot\tools\Build-Windows.ps1" -Configuration $Configuration -Platform $Platform
exit $LASTEXITCODE

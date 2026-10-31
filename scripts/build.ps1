param([ValidateSet('win-x64','win-arm64')][string]$Runtime='win-x64')
& "$PSScriptRoot/build-winui.ps1" -Runtime $Runtime

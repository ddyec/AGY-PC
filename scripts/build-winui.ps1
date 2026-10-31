param([ValidateSet('win-x64','win-arm64')][string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
if ($env:OS -ne 'Windows_NT') { throw 'WinUI 的完整构建需要 Windows，不能在 WSL / Linux 中执行。' }
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Get-Command dotnet -ErrorAction Stop | Out-Null
$platform=if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }
dotnet run --project tests/AGY.PC.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw '核心测试失败，未打包。' }
$output=Join-Path (Get-Location) "dist/winui-$Runtime"
dotnet publish src/AGY.PC.WinUI -c Release -p:Platform=$platform -r $Runtime --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'WinUI 编译失败，请查看上面的日志。' }
$diagnostics=Join-Path (Split-Path $output -Parent) 'startup-diagnostics'
$env:AGY_PC_DIAGNOSTICS_DIR=$diagnostics
$process=Start-Process -FilePath "$output/AGY-PC.exe" -ArgumentList '--ui-check' -WorkingDirectory $output -PassThru
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'WinUI 界面检查超时。' }
if ($process.ExitCode -ne 0) { & "$PSScriptRoot/collect-winui-diagnostics.ps1" -RuntimeDirectory $output -Destination $diagnostics -NoLaunch -ExpectXaml; throw 'WinUI 界面检查失败，请查看 startup-diagnostics/diagnostic-summary.txt。' }
foreach ($image in @('login-light.png','login-dark.png','workspace-dark.png','conversation-dark.png','files-dark.png','workspace-narrow.png','report.json')) {
    if (-not (Test-Path "$output/ui-check/$image")) { throw "缺少界面检查结果：$image。不是已验证运行包。" }
}
Copy-Item README.md "$output/使用说明.md" -Force
Copy-Item THIRD_PARTY_NOTICES.md $output -Force
Copy-Item docs/licenses -Destination $output -Recurse -Force
$archive=Join-Path (Get-Location) "dist/AGY-PC-2.0.0-beta.9-$Runtime.zip"
if (Test-Path $archive) { Remove-Item $archive }
Compress-Archive -Path "$output/*" -DestinationPath $archive
Write-Host "Windows 构建完成：$archive"
Write-Host "请人工查看 $output/ui-check 中的浅色和深色截图；截图生成不代表全部交互已验收。"

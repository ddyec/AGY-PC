param([string]$BuildDirectory)
$ErrorActionPreference='Stop'
try {
    if ($env:OS -ne 'Windows_NT') { throw '请在 Windows 中运行。' }
    $base=Join-Path $env:LOCALAPPDATA 'AGY-PC\WindowsBuilds'
    if (-not $BuildDirectory) {
        $candidates=@(Get-ChildItem -LiteralPath $base -Directory | Sort-Object Name -Descending | Where-Object { Test-Path (Join-Path $_.FullName 'source\AGY-PC\src\AGY.PC.WinUI\AGY.PC.WinUI.csproj') })
        if ($candidates.Count -eq 0) { throw '未找到已有构建目录，请使用新的 Windows Builder。' }
        $BuildDirectory=$candidates[0].FullName
    }
    $BuildDirectory=[IO.Path]::GetFullPath($BuildDirectory)
    $allowed=[IO.Path]::GetFullPath($base).TrimEnd('\')+'\'
    if (-not $BuildDirectory.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw '只修复当前用户的 AGY-PC WindowsBuilds 目录。' }
    $project=Join-Path $BuildDirectory 'source\AGY-PC'
    $payload=Join-Path $PSScriptRoot 'patch'
    $backup=Join-Path $BuildDirectory ('ui-update-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
    $files=@('src\AGY.PC.WinUI\AGY.PC.WinUI.csproj','src\AGY.PC.WinUI\App.xaml','src\AGY.PC.WinUI\App.xaml.cs','src\AGY.PC.WinUI\Program.cs','src\AGY.PC.WinUI\StartupLog.cs','src\AGY.PC.WinUI\MainWindow.cs','src\AGY.PC.WinUI\Ui.cs','src\AGY.PC.Core\StartupChecks.cs','src\AGY.PC.Core\NetworkTransport.cs','src\AGY.PC.Core\GatewayClient.cs','src\AGY.PC.Core\Models.cs','src\AGY.PC.Core\Workspace.cs','tests\AGY.PC.Tests\Program.cs','scripts\collect-winui-diagnostics.ps1','src\AGY.PC.WinUI\DesktopShell.cs','src\AGY.PC.WinUI\VisualPreview.cs','src\AGY.PC.WinUI\FileBrowser.cs','src\AGY.PC.WinUI\BuildPane.cs')
    foreach ($relative in $files) {
        $source=Join-Path $payload $relative
        if (-not (Test-Path -LiteralPath $source)) { throw "修复包缺少 $relative，请先完整解压。" }
    }
    foreach ($relative in $files) {
        $source=Join-Path $payload $relative
        $target=Join-Path $project $relative
        if (Test-Path -LiteralPath $target) { $copy=Join-Path $backup $relative;[void](New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force);Copy-Item -LiteralPath $target -Destination $copy }
        [void](New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force)
        Copy-Item -LiteralPath $source -Destination $target -Force
    }
    foreach ($folder in @('obj','bin')) {
        $generated=Join-Path $project "src\AGY.PC.WinUI\$folder"
        if (Test-Path -LiteralPath $generated) { Remove-Item -LiteralPath $generated -Recurse -Force }
    }
    $cached=Join-Path $env:LOCALAPPDATA 'AGY-PC\BuildTools\dotnet-10.0.403\dotnet.exe'
    $dotnet=if (Test-Path -LiteralPath $cached) { $cached } else { (Get-Command dotnet -ErrorAction Stop).Source }
    $output=Join-Path $BuildDirectory 'AGY-PC-WinUI-Beta6'
    $diagnostics=Join-Path $BuildDirectory 'startup-diagnostics'
    $collector=Join-Path $project 'scripts\collect-winui-diagnostics.ps1'
    Push-Location $project
    try {
        & $dotnet publish src/AGY.PC.WinUI -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -o $output 2>&1 | Tee-Object -FilePath (Join-Path $BuildDirectory 'update-ui-build.log')
        if ($LASTEXITCODE -ne 0) { throw '完整 Windows 构建未通过，请提供 update-ui-build.log 的错误。' }
    } finally { Pop-Location }
    $old=$env:AGY_PC_DIAGNOSTICS_DIR
    try {
        $env:AGY_PC_DIAGNOSTICS_DIR=$diagnostics
        $check=Start-Process -FilePath (Join-Path $output 'AGY-PC.exe') -ArgumentList '--ui-check' -WorkingDirectory $output -PassThru
        if (-not $check.WaitForExit(60000)) { $check.Kill();throw '启动检查超时。' }
        if ($check.ExitCode -ne 0) { $hex=([BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$check.ExitCode),0)).ToString('X8');throw "启动失败：$($check.ExitCode) (0x$hex)" }
        foreach ($name in @('login-light.png','login-dark.png','workspace-dark.png','conversation-dark.png','files-dark.png','workspace-narrow.png','report.json')) {
            if (-not (Test-Path (Join-Path $output "ui-check\$name"))) { throw "缺少 $name，界面检查尚未完成。" }
        }
    } finally { $env:AGY_PC_DIAGNOSTICS_DIR=$old }
    Write-Host "已生成新版客户端和各页面截图，请查看实际界面：$output"
    Start-Process explorer.exe -ArgumentList ('"'+(Join-Path $output 'ui-check')+'"')
    Start-Process -FilePath (Join-Path $output 'AGY-PC.exe') -WorkingDirectory $output
} catch {
    Write-Host ('未完成：'+$_.Exception.Message) -ForegroundColor Red
    if ($collector -and (Test-Path -LiteralPath $collector) -and $output -and (Test-Path -LiteralPath $output)) {
        try { & $collector -RuntimeDirectory $output -Destination $diagnostics -NoLaunch -ExpectXaml } catch { Write-Host ('补充诊断未完成：'+$_.Exception.Message) }
    }
    Write-Host "请提供 $diagnostics 中的 diagnostic-summary.txt 和 startup.log；不要删除原构建目录。"
    Read-Host '按 Enter 关闭'
    exit 1
}

param([string]$BuildDirectory)
$ErrorActionPreference='Stop'
try {
    if ($env:OS -ne 'Windows_NT') { throw '请在 Windows 中运行。' }
    $agyRoot=Join-Path $env:LOCALAPPDATA 'AGY-PC'
    $workspaceDir=Join-Path $agyRoot 'Workspace'
    $project=Join-Path $workspaceDir 'AGY-PC'
    $legacyBase=Join-Path $agyRoot 'WindowsBuilds'
    $appOutput=Join-Path $agyRoot 'App'
    $logDir=Join-Path $agyRoot 'Logs'
    [void](New-Item -ItemType Directory -Path $logDir -Force)

    # Stop any running AGY-PC process so files in App output are not locked
    Get-Process -Name 'AGY-PC' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    # Migrate source from legacy WindowsBuilds\<timestamp>\source\AGY-PC if Workspace\AGY-PC doesn't exist yet
    if (-not (Test-Path (Join-Path $project 'src\AGY.PC.WinUI\AGY.PC.WinUI.csproj'))) {
        $sourceCandidate=$null
        if ($BuildDirectory -and (Test-Path (Join-Path $BuildDirectory 'source\AGY-PC\src\AGY.PC.WinUI\AGY.PC.WinUI.csproj'))) {
            $sourceCandidate=Join-Path $BuildDirectory 'source\AGY-PC'
        } elseif (Test-Path -LiteralPath $legacyBase) {
            $candidates=@(Get-ChildItem -LiteralPath $legacyBase -Directory | Sort-Object Name -Descending | Where-Object { Test-Path (Join-Path $_.FullName 'source\AGY-PC\src\AGY.PC.WinUI\AGY.PC.WinUI.csproj') })
            if ($candidates.Count -gt 0) {
                $sourceCandidate=Join-Path $candidates[0].FullName 'source\AGY-PC'
            }
        }
        if (-not $sourceCandidate) { throw '未找到已有源码目录，请运行 AGY-PC-Windows-Builder.exe。' }
        Write-Host "正在将源码工作区迁移至固定目录：$project"
        [void](New-Item -ItemType Directory -Path $workspaceDir -Force)
        if (Test-Path -LiteralPath $project) { Remove-Item -LiteralPath $project -Recurse -Force }
        Copy-Item -LiteralPath $sourceCandidate -Destination $project -Recurse -Force
    }

    # Clean up all accumulated temporary directories in WindowsBuilds
    if (Test-Path -LiteralPath $legacyBase) {
        Write-Host "正在清理旧版临时构建目录 ($legacyBase)…"
        try { Remove-Item -LiteralPath $legacyBase -Recurse -Force -ErrorAction Stop }
        catch { Write-Host "部分旧临时文件正在使用中，稍后将自动清理。" }
    }

    $payload=Join-Path $PSScriptRoot 'patch'
    $files=@(
        'src\AGY.PC.WinUI\AGY.PC.WinUI.csproj',
        'src\AGY.PC.WinUI\App.xaml',
        'src\AGY.PC.WinUI\App.xaml.cs',
        'src\AGY.PC.WinUI\Program.cs',
        'src\AGY.PC.WinUI\StartupLog.cs',
        'src\AGY.PC.WinUI\MainWindow.cs',
        'src\AGY.PC.WinUI\Ui.cs',
        'src\AGY.PC.Core\StartupChecks.cs',
        'src\AGY.PC.Core\NetworkTransport.cs',
        'src\AGY.PC.Core\GatewayClient.cs',
        'src\AGY.PC.Core\Models.cs',
        'src\AGY.PC.Core\Workspace.cs',
        'tests\AGY.PC.Tests\Program.cs',
        'scripts\collect-winui-diagnostics.ps1',
        'src\AGY.PC.WinUI\DesktopShell.cs',
        'src\AGY.PC.WinUI\VisualPreview.cs',
        'src\AGY.PC.WinUI\FileBrowser.cs',
        'src\AGY.PC.WinUI\BuildPane.cs'
    )
    foreach ($relative in $files) {
        $source=Join-Path $payload $relative
        if (-not (Test-Path -LiteralPath $source)) { throw "更新包缺少 $relative，请先完整解压。" }
    }
    foreach ($relative in $files) {
        $source=Join-Path $payload $relative
        $target=Join-Path $project $relative
        [void](New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force)
        Copy-Item -LiteralPath $source -Destination $target -Force
    }
    foreach ($folder in @('src\AGY.PC.WinUI\obj','src\AGY.PC.WinUI\bin','src\AGY.PC.Core\obj','src\AGY.PC.Core\bin')) {
        $generated=Join-Path $project $folder
        if (Test-Path -LiteralPath $generated) { Remove-Item -LiteralPath $generated -Recurse -Force }
    }

    $cached=Join-Path $agyRoot 'BuildTools\dotnet-10.0.403\dotnet.exe'
    $dotnet=if (Test-Path -LiteralPath $cached) { $cached } else { (Get-Command dotnet -ErrorAction Stop).Source }
    $output=$appOutput
    $diagnostics=Join-Path $logDir 'startup-diagnostics'
    $collector=Join-Path $project 'scripts\collect-winui-diagnostics.ps1'

    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    [void](New-Item -ItemType Directory -Path $output -Force)

    Push-Location $project
    try {
        & $dotnet publish src/AGY.PC.WinUI -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -o $output 2>&1 | Tee-Object -FilePath (Join-Path $logDir 'update-ui-build.log')
        if ($LASTEXITCODE -ne 0) { throw '完整 Windows 构建未通过，请查看 Logs\update-ui-build.log。' }
    } finally { Pop-Location }

    # Clean intermediate build obj/bin after publish to keep Workspace lean
    foreach ($folder in @('src\AGY.PC.WinUI\obj','src\AGY.PC.WinUI\bin','src\AGY.PC.Core\obj','src\AGY.PC.Core\bin')) {
        $generated=Join-Path $project $folder
        if (Test-Path -LiteralPath $generated) { Remove-Item -LiteralPath $generated -Recurse -Force -ErrorAction SilentlyContinue }
    }

    $old=$env:AGY_PC_DIAGNOSTICS_DIR
    try {
        $env:AGY_PC_DIAGNOSTICS_DIR=$diagnostics
        $check=Start-Process -FilePath (Join-Path $output 'AGY-PC.exe') -ArgumentList '--ui-check' -WorkingDirectory $output -PassThru
        if (-not $check.WaitForExit(60000)) { $check.Kill();throw '启动自检超时。' }
        if ($check.ExitCode -ne 0) { $hex=([BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$check.ExitCode),0)).ToString('X8');throw "启动失败：$($check.ExitCode) (0x$hex)" }
    } finally { $env:AGY_PC_DIAGNOSTICS_DIR=$old }

    # Remove temporary ui-check screenshots from the clean runtime folder
    $uiCheckDir=Join-Path $output 'ui-check'
    if (Test-Path -LiteralPath $uiCheckDir) { Remove-Item -LiteralPath $uiCheckDir -Recurse -Force -ErrorAction SilentlyContinue }

    # Also copy the compiled app right next to Update-UI.cmd (<current folder>\AGY-PC-App)
    $localApp=Join-Path $PSScriptRoot 'AGY-PC-App'
    try {
        if (Test-Path -LiteralPath $localApp) { Remove-Item -LiteralPath $localApp -Recurse -Force }
        Copy-Item -LiteralPath $output -Destination $localApp -Recurse -Force
        Write-Host "已同步生成本地便携目录：$localApp" -ForegroundColor Green
    } catch {
        Write-Host "提示：复制到当前目录跳过，固定程序位于：$output"
    }

    Write-Host "更新完成！固定安装目录：$output" -ForegroundColor Green
    $launchDir=if (Test-Path (Join-Path $localApp 'AGY-PC.exe')) { $localApp } else { $output }
    Start-Process -FilePath (Join-Path $launchDir 'AGY-PC.exe') -WorkingDirectory $launchDir
} catch {
    Write-Host ('未完成：'+$_.Exception.Message) -ForegroundColor Red
    if ($collector -and (Test-Path -LiteralPath $collector) -and $output -and (Test-Path -LiteralPath $output)) {
        try { & $collector -RuntimeDirectory $output -Destination $diagnostics -NoLaunch -ExpectXaml } catch { Write-Host ('补充诊断未完成：'+$_.Exception.Message) }
    }
    Write-Host "请查看 $diagnostics 中的 diagnostic-summary.txt 和 startup.log。"
    Read-Host '按 Enter 关闭'
    exit 1
}

param([string]$RuntimeDirectory,[string]$Destination,[switch]$NoLaunch,[switch]$ExpectXaml)
$ErrorActionPreference='Stop'
if (-not $RuntimeDirectory) {
    $base=Join-Path $env:LOCALAPPDATA 'AGY-PC\WindowsBuilds'
    $candidates=@(Get-ChildItem -LiteralPath $base -Directory | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Directory -Filter 'AGY-PC-WinUI*' } | Where-Object { Test-Path (Join-Path $_.FullName 'AGY-PC.exe') } | Sort-Object LastWriteTime -Descending)
    if ($candidates.Count -eq 0) { throw '未找到已经发布的 AGY-PC.exe。' }
    $RuntimeDirectory=$candidates[0].FullName
}
$RuntimeDirectory=[IO.Path]::GetFullPath($RuntimeDirectory)
if (-not $Destination) { $Destination=Join-Path (Split-Path $RuntimeDirectory -Parent) ('startup-diagnostics-'+(Get-Date -Format 'yyyyMMdd-HHmmss')) }
[void](New-Item -ItemType Directory -Path $Destination -Force)
$expected=@('AGY-PC.exe','AGY-PC.dll','AGY-PC.runtimeconfig.json','AGY-PC.deps.json','AGY.PC.Core.dll','AGY-PC.pri','Microsoft.UI.Xaml.dll','Microsoft.UI.Xaml.Controls.pri','Assets\Mark.png','Assets\AGY.ico')
if ($ExpectXaml) { $expected+='App.xbf' }
$missing=@($expected | Where-Object { -not (Test-Path -LiteralPath (Join-Path $RuntimeDirectory $_)) })
$layout=@(Get-ChildItem -LiteralPath $RuntimeDirectory -File -Recurse | Select-Object -First 2000 | ForEach-Object { [PSCustomObject]@{path=$_.FullName.Substring($RuntimeDirectory.Length).TrimStart('\');bytes=$_.Length} })
$layout | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Destination 'published-files.json') -Encoding UTF8
$summary=New-Object System.Collections.Generic.List[string]
$summary.Add('Runtime: '+$RuntimeDirectory)
$summary.Add('Windows: '+[Environment]::OSVersion.VersionString)
$summary.Add('64-bit OS: '+[Environment]::Is64BitOperatingSystem)
$summary.Add('Missing required files: '+($missing -join ', '))
$exitCode=$null
if (-not $NoLaunch) {
    $old=$env:AGY_PC_DIAGNOSTICS_DIR
    try {
        $env:AGY_PC_DIAGNOSTICS_DIR=$Destination
        $process=Start-Process -FilePath (Join-Path $RuntimeDirectory 'AGY-PC.exe') -ArgumentList '--ui-check' -WorkingDirectory $RuntimeDirectory -PassThru
        if (-not $process.WaitForExit(60000)) { $process.Kill();$summary.Add('UI check timed out after 60 seconds.') }
        else { $exitCode=$process.ExitCode;$hex=([BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$exitCode),0)).ToString('X8');$summary.Add("Exit: $exitCode (0x$hex)") }
    } catch { $summary.Add('Launch failed: '+$_.Exception.Message) }
    finally { $env:AGY_PC_DIAGNOSTICS_DIR=$old }
}
Start-Sleep -Seconds 2
try {
    $events=@(Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=(Get-Date).AddMinutes(-10);Id=@(1000,1001,1026)} -ErrorAction Stop | Where-Object { $_.Message -match 'AGY-PC(\.exe|\.dll)|AGY\.PC\.WinUI' } | Select-Object -First 12 | ForEach-Object { [PSCustomObject]@{time=$_.TimeCreated.ToString('O');id=$_.Id;provider=$_.ProviderName;message=$_.Message} })
    $events | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Destination 'windows-events.json') -Encoding UTF8
    foreach ($event in $events) { $summary.Add("Event $($event.id) $($event.time)");$summary.Add($event.message) }
} catch { $summary.Add('Application event log unavailable: '+$_.Exception.Message) }
$uiError=Join-Path $RuntimeDirectory 'ui-check-error.txt'
if (Test-Path -LiteralPath $uiError) { Copy-Item -LiteralPath $uiError -Destination $Destination -Force;$summary.Add((Get-Content -LiteralPath $uiError -Raw)) }
$startup=Join-Path $Destination 'startup.log'
if (Test-Path -LiteralPath $startup) { $summary.Add('--- Startup stages / original exceptions ---');$summary.Add((Get-Content -LiteralPath $startup -Raw)) }
else { $summary.Add('No managed startup log. If this is beta.2, the executable had no startup instrumentation; check Windows events and file layout.') }
$text=$summary -join [Environment]::NewLine
$text | Set-Content -LiteralPath (Join-Path $Destination 'diagnostic-summary.txt') -Encoding UTF8
$zip=$Destination+'.zip'
Compress-Archive -Path (Join-Path $Destination '*') -DestinationPath $zip -Force
Write-Host $text
Write-Host "诊断包：$zip"
Write-Host '只采集文件清单、启动异常和本应用事件，不读取连接密钥、不修改注册表、不上传任何数据。'

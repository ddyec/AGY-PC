param([string]$BuildDirectory)
$ErrorActionPreference='Stop'
try {
    if ($env:OS -ne 'Windows_NT') { throw '此修复脚本需要在 Windows 中运行。' }
    $root=Join-Path $env:LOCALAPPDATA 'AGY-PC\WindowsBuilds'
    if (-not $BuildDirectory) {
        $candidates=@(Get-ChildItem -LiteralPath $root -Directory | Sort-Object Name -Descending | Where-Object { Test-Path (Join-Path $_.FullName 'source\AGY-PC\src\AGY.PC.WinUI\AGY.PC.WinUI.csproj') })
        if ($candidates.Count -eq 0) { throw '未找到之前的 Windows 构建目录。请运行新版 Windows Builder。' }
        $BuildDirectory=$candidates[0].FullName
    }
    $BuildDirectory=[IO.Path]::GetFullPath($BuildDirectory)
    $allowed=[IO.Path]::GetFullPath($root).TrimEnd('\')+'\'
    if (-not $BuildDirectory.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw '只修复当前用户 AGY-PC 的 WindowsBuilds 目录。' }
    $project=Join-Path $BuildDirectory 'source\AGY-PC'
    $projectFile=Join-Path $project 'src\AGY.PC.WinUI\AGY.PC.WinUI.csproj'
    if (-not (Test-Path -LiteralPath $projectFile)) { throw '未找到 WinUI 项目文件。' }
    $backup=Join-Path $BuildDirectory 'AGY.PC.WinUI.csproj.before-PRI175'
    if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $projectFile -Destination $backup }
    $document=New-Object System.Xml.XmlDocument
    $document.PreserveWhitespace=$true
    $document.Load($projectFile)
    $mark=$document.SelectSingleNode('/Project/ItemGroup/Content[@Include="Assets/Mark.png"]')
    if (-not $mark) { throw '项目结构与此修复不匹配，未改动。请运行新版 Windows Builder。' }
    $group=$mark.ParentNode
    foreach ($kind in @('Content','None')) {
        if (-not $group.SelectSingleNode("${kind}[@Remove='Assets/**']")) {
            $remove=$document.CreateElement($kind)
            $remove.SetAttribute('Remove','Assets/**')
            [void]$group.PrependChild($remove)
        }
    }
    foreach ($item in $group.SelectNodes('Content[starts-with(@Include,"Assets/")]')) {
        $item.SetAttribute('CopyToOutputDirectory','PreserveNewest')
        $item.SetAttribute('CopyToPublishDirectory','PreserveNewest')
    }
    $version=$document.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($version) { $version.InnerText='2.0.0-beta.3' }
    $document.Save($projectFile)
    foreach ($folder in @('obj','bin')) {
        $generated=Join-Path $project "src\AGY.PC.WinUI\$folder"
        if (Test-Path -LiteralPath $generated) { Remove-Item -LiteralPath $generated -Recurse -Force }
    }
    $cached=Join-Path $env:LOCALAPPDATA 'AGY-PC\BuildTools\dotnet-10.0.403\dotnet.exe'
    $dotnet=if (Test-Path -LiteralPath $cached) { $cached } else { (Get-Command dotnet -ErrorAction Stop).Source }
    $output=Join-Path $BuildDirectory 'AGY-PC-WinUI-PRI175-Fixed'
    Write-Host "修复完成，复用 SDK 和 NuGet 缓存重新构建：$BuildDirectory"
    Push-Location $project
    try {
        & $dotnet publish src/AGY.PC.WinUI -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -o $output 2>&1 | Tee-Object -FilePath (Join-Path $BuildDirectory 'repair-PRI175.log')
        if ($LASTEXITCODE -ne 0) { throw 'Windows 构建仍未完成，请提供 repair-PRI175.log 中的错误。' }
    } finally { Pop-Location }
    $exe=Join-Path $output 'AGY-PC.exe'
    $check=Start-Process -FilePath $exe -ArgumentList '--ui-check' -WorkingDirectory $output -PassThru
    if (-not $check.WaitForExit(60000)) { $check.Kill(); throw 'WinUI 界面检查超时。' }
    foreach ($name in @('login-light.png','login-dark.png','report.json')) {
        if (-not (Test-Path (Join-Path $output "ui-check\$name"))) { throw "未生成 $name，请提供输出目录中的 ui-check-error.txt。" }
    }
    Write-Host "运行目录：$output"
    Start-Process explorer.exe -ArgumentList ('"'+(Join-Path $output 'ui-check')+'"')
    Start-Process -FilePath $exe -WorkingDirectory $output
} catch {
    Write-Host ('未完成：'+$_.Exception.Message) -ForegroundColor Red
    Read-Host '按 Enter 关闭'
    exit 1
}

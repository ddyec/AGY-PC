param([ValidateSet('win-x64','win-arm64')][string]$Runtime='win-x64')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
Get-Command dotnet -ErrorAction Stop | Out-Null
dotnet run --project tests/AGY.PC.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw '核心测试失败，未打包。' }
$output=Join-Path (Get-Location) "dist/$Runtime"
dotnet publish src/AGY.PC.Windows -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Windows 客户端编译失败。' }
Copy-Item README.md "$output/使用说明.md" -Force
Copy-Item THIRD_PARTY_NOTICES.md $output -Force
Copy-Item docs/licenses -Destination $output -Recurse -Force
$archive=Join-Path (Get-Location) "dist/AGY-PC-1.0.0-$Runtime.zip"
if (Test-Path $archive) { Remove-Item $archive }
Compress-Archive -Path "$output/*" -DestinationPath $archive
Write-Host "运行包：$archive"

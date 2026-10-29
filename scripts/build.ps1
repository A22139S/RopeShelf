param([string]$Dotnet = 'dotnet', [switch]$Publish, [string]$Destination, [string]$Feed)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'artifacts/cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot 'artifacts/packages'
$env:APPDATA = Join-Path $projectRoot 'artifacts/appdata'
$env:LOCALAPPDATA = Join-Path $projectRoot 'artifacts/local'
$common = @('-c','Release','--disable-build-servers','-m:1','--configfile', (Join-Path $projectRoot 'NuGet.Config'), '-p:TargetPlatformMoniker=Windows%2CVersion=7.0', '-p:NuGetAudit=false')
if ($Feed) { $common += @('--source', $Feed) }
& $Dotnet build (Join-Path $projectRoot 'src/RopeShelf.App/RopeShelf.App.csproj') @common
if ($LASTEXITCODE) { throw '应用构建失败' }
& $Dotnet build (Join-Path $projectRoot 'tests/RopeShelf.Tests/RopeShelf.Tests.csproj') @common
if ($LASTEXITCODE) { throw '测试构建失败' }
& $Dotnet (Join-Path $projectRoot 'tests/RopeShelf.Tests/bin/Release/net10.0-windows/RopeShelf.Tests.dll') (Join-Path $projectRoot 'artifacts/tests')
if ($LASTEXITCODE) { throw '测试失败' }
if ($Publish) {
    if (!$Destination) { $Destination = Join-Path $projectRoot 'artifacts/publish' }
    & $Dotnet publish (Join-Path $projectRoot 'src/RopeShelf.App/RopeShelf.App.csproj') @common -r win-x64 --self-contained true -o $Destination
    if ($LASTEXITCODE) { throw '自包含发布失败' }
}

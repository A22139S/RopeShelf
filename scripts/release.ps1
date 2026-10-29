param([string]$Dotnet = 'dotnet', [string]$Feed)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/RopeShelf.App/RopeShelf.App.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$publishRoot = Join-Path $projectRoot ('artifacts/publish-' + [guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'build.ps1') -Dotnet $Dotnet -Feed $Feed -Publish -Destination $publishRoot
if ($LASTEXITCODE) { throw 'Build or publish failed' }
$releaseRoot = Join-Path $projectRoot 'artifacts/releases'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$bundle = Join-Path $releaseRoot ('RopeShelf-' + $version + '-win-x64-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $bundle | Out-Null
Get-ChildItem -LiteralPath $publishRoot -File | Where-Object { $_.Extension -ne '.pdb' } | Copy-Item -Destination $bundle
foreach ($name in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $bundle
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/manual-acceptance.md') -Destination (Join-Path $bundle 'manual-acceptance.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manual-check.cmd') -Destination $bundle
$licenseRoot = Join-Path $bundle 'licenses'
New-Item -ItemType Directory -Path $licenseRoot | Out-Null
[object]$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishRoot 'RopeShelf.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    $frameworkName = if ($runtime -eq 'microsoft.netcore.app.runtime.win-x64') { 'Microsoft.NETCore.App' } else { 'Microsoft.WindowsDesktop.App' }
    $runtimeVersion = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq $frameworkName })[0].version
    if (!$runtimeVersion) { throw "Missing included framework: $frameworkName" }
    $runtimeRoot = Join-Path $projectRoot ('artifacts/packages/' + $runtime + '/' + $runtimeVersion)
    $licenseFiles = @(Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse | Where-Object { $_.Name -match '(?i)license|notice' })
    if (!$licenseFiles.Count) { throw "Missing runtime licenses: $runtime" }
    foreach ($file in $licenseFiles) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $licenseRoot ($runtime + '-' + $runtimeVersion + '-' + $file.Name))
    }
}
$zip = Join-Path $releaseRoot ('RopeShelf-' + $version + '-win-x64.zip')
Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath $zip -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
($hash + '  ' + (Split-Path $zip -Leaf)) | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii
Write-Output $zip

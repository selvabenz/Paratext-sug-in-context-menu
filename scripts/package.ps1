[CmdletBinding()]
param([string]$Version = '0.2.0-preview.2', [string]$ParatextDir = 'C:\Program Files\Paratext 9')
$ErrorActionPreference = 'Stop'
if ($Version -ne '0.2.0-preview.2') { throw 'Update assembly metadata, documentation, and package version together before changing the version.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -ParatextDir $ParatextDir
$releaseDir = Join-Path $projectRoot ("artifacts\release\stage-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'artifacts\bin\ParatextNoteSuggestions.ptxplg') -Destination $releaseDir -Force
foreach ($name in @('install.ps1','uninstall.ps1')) { Copy-Item -LiteralPath (Join-Path $projectRoot ("packaging\" + $name)) -Destination $releaseDir -Force }
foreach ($name in @('README.md','INSTALL.md','DEVELOPER_HANDOFF.md','RELEASE_NOTES.md','VALIDATION.md','CATEGORY_HANDLING.md','PLAN.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $releaseDir -Force }
$checksums = @(Get-ChildItem -LiteralPath $releaseDir -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name })
[IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), $checksums, [Text.Encoding]::ASCII)
$zipFile = Join-Path (Split-Path -Parent $releaseDir) ("ParatextNoteSuggestions-" + $Version + '.zip')
Compress-Archive -Path (Join-Path $releaseDir '*') -DestinationPath $zipFile -Force
$zipHash = "{0}  {1}" -f (Get-FileHash -LiteralPath $zipFile -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path -Leaf $zipFile)
[IO.File]::WriteAllText(($zipFile + '.sha256'), $zipHash + [Environment]::NewLine, [Text.Encoding]::ASCII)
Write-Output "Release archive: $zipFile"

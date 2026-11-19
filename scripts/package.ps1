[CmdletBinding()]
param([string]$ParatextDir = 'C:\Program Files\Paratext 9', [switch]$RequireInstaller)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = & (Join-Path $PSScriptRoot 'version.ps1') -Root $projectRoot
& (Join-Path $PSScriptRoot 'build.ps1') -ParatextDir $ParatextDir
$releaseDir = Join-Path $projectRoot ("artifacts\release\stage-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'artifacts\bin\ParatextNoteSuggestions.ptxplg') -Destination $releaseDir -Force
foreach ($name in @('install.ps1','uninstall.ps1')) { Copy-Item -LiteralPath (Join-Path $projectRoot ("packaging\" + $name)) -Destination $releaseDir -Force }
foreach ($name in @('LICENSE','README.md','INSTALL.md','DEVELOPER_HANDOFF.md','RELEASE_NOTES.md','VALIDATION.md','CATEGORY_HANDLING.md','PLAN.md','ISSUES_1_4.md','DECISION_COMMENTS_PLAN.md','SANDHI_BULK_PLAN.md','CONFIRMED_TYPO_PLAN.md','SELECTED_BULK_PLAN.md','TARGET_HIGHLIGHT_PLAN.md','NAVIGATION_FIX_REPORT.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $releaseDir -Force }
$checksums = @(Get-ChildItem -LiteralPath $releaseDir -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name })
[IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), $checksums, [Text.Encoding]::ASCII)
# Everything published for this version goes in one folder: the ZIP (install.ps1), the Windows
# installer, and a .sha256 sidecar for each.
$distDir = Join-Path $projectRoot ("artifacts\release\" + $version.Version)
if (Test-Path -LiteralPath $distDir) { Remove-Item -LiteralPath $distDir -Recurse -Force }
New-Item -ItemType Directory -Path $distDir -Force | Out-Null
$zipFile = Join-Path $distDir ("ParatextNoteSuggestions-" + $version.Version + '.zip')
Compress-Archive -Path (Join-Path $releaseDir '*') -DestinationPath $zipFile -Force
$compiler = @($env:ISCC, (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if ($compiler) {
    & $compiler /Q ("/DAppVersion=" + $version.Version) ("/DFileVersion=" + $version.FileVersion) ("/DSourceDir=" + $releaseDir) ("/DOutputDir=" + $distDir) (Join-Path $projectRoot 'installer\ParatextNoteSuggestions.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Windows installer compilation failed.' }
}
elseif ($RequireInstaller) { throw 'Inno Setup 6 (ISCC.exe) is required to build the Windows installer.' }
else { Write-Warning 'Inno Setup 6 was not found; only the ZIP package was built.' }
foreach ($file in Get-ChildItem -LiteralPath $distDir -File | Where-Object { $_.Extension -in '.zip', '.exe' }) {
    $line = "{0}  {1}" -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name
    # LF line ending, so both Get-FileHash comparisons and `sha256sum -c` accept the file.
    [IO.File]::WriteAllText(($file.FullName + '.sha256'), $line + "`n", [Text.Encoding]::ASCII)
    Write-Output "Release file: $($file.FullName)"
}

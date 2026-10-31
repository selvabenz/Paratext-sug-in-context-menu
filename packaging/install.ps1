[CmdletBinding(SupportsShouldProcess = $true)]
param([string]$ParatextDir = 'C:\Program Files\Paratext 9')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Paratext -ErrorAction SilentlyContinue) { throw 'Close Paratext yourself before installing. This script will not close it or save your projects.' }
$sourceFile = Join-Path $PSScriptRoot 'ParatextNoteSuggestions.ptxplg'
if (!(Test-Path -LiteralPath $sourceFile -PathType Leaf)) { throw 'Run this script from the extracted release folder containing ParatextNoteSuggestions.ptxplg.' }
$checksumFile = Join-Path $PSScriptRoot 'SHA256SUMS.txt'
if (!(Test-Path -LiteralPath $checksumFile -PathType Leaf)) { throw 'The release checksum manifest is missing.' }
$hashLine = @(Get-Content -LiteralPath $checksumFile | Where-Object { $_ -match '^[a-fA-F0-9]{64}\s+ParatextNoteSuggestions\.ptxplg$' })
if ($hashLine.Count -ne 1) { throw 'The plugin checksum entry is missing or ambiguous.' }
$expectedHash = ($hashLine[0] -split '\s+')[0]
if ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne $expectedHash) { throw 'The plugin checksum does not match the release manifest.' }
$hostExe = Join-Path $ParatextDir 'Paratext.exe'
if (!(Test-Path -LiteralPath $hostExe -PathType Leaf)) { throw "Paratext was not found at $ParatextDir" }
$hostVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($hostExe).FileVersion
if ($hostVersion -ne '9.5.110.1') { throw "This preview was built against 9.5.110.1; found $hostVersion. Verify compatibility before installing." }
$resolvedHost = (Resolve-Path -LiteralPath $ParatextDir).Path
$destinationDir = Join-Path $resolvedHost 'Plugins\ParatextNoteSuggestions'
$destinationFile = Join-Path $destinationDir 'ParatextNoteSuggestions.ptxplg'
if ($PSCmdlet.ShouldProcess($destinationFile, 'Install Note Suggestions preview plugin')) {
    New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
    Copy-Item -LiteralPath $sourceFile -Destination $destinationFile -Force
    Write-Output "Installed $destinationFile. Start Paratext, then use a disposable test project first."
}

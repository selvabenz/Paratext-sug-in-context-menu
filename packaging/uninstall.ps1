[CmdletBinding(SupportsShouldProcess = $true)]
param([string]$ParatextDir = 'C:\Program Files\Paratext 9')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Paratext -ErrorAction SilentlyContinue) { throw 'Close Paratext yourself before uninstalling.' }
$resolvedHost = (Resolve-Path -LiteralPath $ParatextDir).Path
$pluginFile = Join-Path $resolvedHost 'Plugins\ParatextNoteSuggestions\ParatextNoteSuggestions.ptxplg'
if (!(Test-Path -LiteralPath $pluginFile -PathType Leaf)) { Write-Output 'Note Suggestions is not installed at this location.'; return }
if ($PSCmdlet.ShouldProcess($pluginFile, 'Remove only the Note Suggestions plugin file')) { Remove-Item -LiteralPath $pluginFile; Write-Output 'Plugin removed. Project text and project history have not been changed.' }

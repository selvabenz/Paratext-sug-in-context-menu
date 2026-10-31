[CmdletBinding(SupportsShouldProcess = $true)]
param([string]$ParatextDir = 'C:\Program Files\Paratext 9')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Paratext -ErrorAction SilentlyContinue) { throw 'Close Paratext yourself before uninstalling.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (!$WhatIfPreference -and !$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File', ('"' + $PSCommandPath + '"'), '-ParatextDir', ('"' + $ParatextDir + '"'))
    $elevated = Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -PassThru
    $elevated.WaitForExit()
    if ($elevated.ExitCode -ne 0) { throw 'The elevated uninstaller failed or was cancelled.' }
    return
}
$resolvedHost = (Resolve-Path -LiteralPath $ParatextDir).Path
$pluginFile = Join-Path $resolvedHost 'Plugins\ParatextNoteSuggestions\ParatextNoteSuggestions.ptxplg'
if (!(Test-Path -LiteralPath $pluginFile -PathType Leaf)) { Write-Output 'Note Suggestions is not installed at this location.'; return }
if ($PSCmdlet.ShouldProcess($pluginFile, 'Remove only the Note Suggestions plugin file')) {
    Remove-Item -LiteralPath $pluginFile
    $cacheUpdater = Join-Path $resolvedHost 'UpdateParatextPluginCache.exe'
    if (Test-Path -LiteralPath $cacheUpdater -PathType Leaf) { & $cacheUpdater; if ($LASTEXITCODE -ne 0) { throw 'The file was removed, but plugin-cache refresh failed.' } }
    Write-Output 'Plugin removed. Project text and project history have not been changed.'
}

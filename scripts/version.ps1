[CmdletBinding()]
param([string]$Root)
$ErrorActionPreference = 'Stop'
if (!$Root) { $Root = Split-Path -Parent $PSScriptRoot }
# VERSION holds MAJOR.MINOR with an optional label, such as 0.4-preview. The patch number counts the
# commits since VERSION last changed, so every push to main gets a new, reproducible version
# (0.4.0-preview, 0.4.1-preview, ...). Bump VERSION to start a new minor or major series.
$raw = (Get-Content -LiteralPath (Join-Path $Root 'VERSION') -TotalCount 1).Trim()
if ($raw -notmatch '^(\d+)\.(\d+)(?:-([0-9A-Za-z][0-9A-Za-z.]*))?$') { throw "VERSION must look like 0.4 or 0.4-preview; found '$raw'." }
$major = [int]$Matches[1]; $minor = [int]$Matches[2]; $label = $Matches[3]
$patch = 0
if (Get-Command git -ErrorAction SilentlyContinue) {
    # Outside a Git checkout (for example an extracted source ZIP) the patch number is 0. Windows
    # PowerShell turns redirected git errors into exceptions under 'Stop', so relax it here.
    # A shallow clone would undercount; CI checks out the full history.
    $preference = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try {
        $changed = & git -C $Root log -n 1 --format=%H -- VERSION 2>$null
        if ($LASTEXITCODE -eq 0 -and $changed) {
            $count = & git -C $Root rev-list --count "$changed..HEAD" 2>$null
            if ($LASTEXITCODE -eq 0) { $patch = [int]$count }
        }
    }
    finally { $ErrorActionPreference = $preference }
}
$version = "$major.$minor.$patch"
if ($label) { $version += "-$label" }
[pscustomobject]@{
    Version = $version
    FileVersion = "$major.$minor.$patch.0"
    Major = $major; Minor = $minor; Patch = $patch
    Prerelease = [bool]$label
    Tag = "v$version"
}

[CmdletBinding()]
param([string]$Destination)
$ErrorActionPreference = 'Stop'
if (!$Destination) { $Destination = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\paratext-interfaces' }
# Builds without a Paratext installation (CI) use the plugin interfaces the Paratext team publishes on
# nuget.org. They are pinned to the hashes of the 2.0.100.0 assemblies installed with Paratext 9.5.110.1,
# which the plugin is tested against; any other file stops the build.
$packages = @(
    @{ Id = 'ParatextCorePluginInterfaces'; Entry = 'lib/netstandard2.0/CorePluginInterfaces.dll'; Sha256 = 'C32E82C9C3D98A983B7AB0D10F6BF9CB03D72209D7E3AF1BADED9312F314282A' },
    @{ Id = 'ParatextPluginInterfaces'; Entry = 'lib/netstandard2.0/PluginInterfaces.dll'; Sha256 = 'F30DA62596DB7B84151DB205AFA4C3FC4A09751D367A8B2F255E32C5E48F0B32' },
    @{ Id = 'ParatextEmbeddedUiPluginInterfaces'; Entry = 'lib/net48/EmbeddedUiPluginInterfaces.dll'; Sha256 = '2A67E279C77DAE81958E325443D245D7B60F9281A2881FB42F1D37D8DFE6D13B' }
)
$packageVersion = '2.0.100'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$Destination = (Resolve-Path -LiteralPath $Destination).Path
foreach ($package in $packages) {
    $id = $package.Id.ToLowerInvariant()
    $nupkg = Join-Path ([IO.Path]::GetTempPath()) "$id.$packageVersion.nupkg"
    Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/$id/$packageVersion/$id.$packageVersion.nupkg" -OutFile $nupkg
    $target = Join-Path $Destination (Split-Path -Leaf $package.Entry)
    $zip = [IO.Compression.ZipFile]::OpenRead($nupkg)
    try {
        $entry = $zip.GetEntry($package.Entry)
        if (!$entry) { throw "$($package.Id) $packageVersion does not contain $($package.Entry)." }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
    finally { $zip.Dispose(); Remove-Item -LiteralPath $nupkg -Force -ErrorAction SilentlyContinue }
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($hash -ne $package.Sha256) { Remove-Item -LiteralPath $target -Force; throw "$($package.Id) $packageVersion has an unexpected SHA-256 ($hash). Review compatibility before changing the pin." }
}
Write-Output $Destination

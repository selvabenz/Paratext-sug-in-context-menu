[CmdletBinding()]
param([string]$ParatextDir = 'C:\Program Files\Paratext 9')
$ErrorActionPreference = 'Stop'
foreach ($name in @('CorePluginInterfaces.dll','PluginInterfaces.dll')) {
    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $ParatextDir $name))
    $assembly.FullName
    foreach ($type in $assembly.GetExportedTypes() | Where-Object Name -In @('IProject','IPluginHost','IProjectNote','IScriptureTextSelection','PluginMenuEntry','IUSFMToken','IUSFMTextToken','IUSFMMarkerToken')) {
        "TYPE $($type.FullName)"
        $type.GetMembers() | ForEach-Object ToString
    }
}

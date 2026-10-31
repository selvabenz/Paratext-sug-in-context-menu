[CmdletBinding()]
param([string]$ParatextDir = 'C:\Program Files\Paratext 9', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $projectRoot 'artifacts\bin'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkDir = Split-Path -Parent $compilerPath
if (!(Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework C# compiler is required (Windows with .NET Framework 4.8).' }
$refs = @('CorePluginInterfaces.dll', 'PluginInterfaces.dll')
foreach ($name in $refs) {
    $path = Join-Path $ParatextDir $name
    if (!(Test-Path -LiteralPath $path)) { throw "Missing installed Paratext interface: $path" }
    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($path)
    if ($assemblyName.Version -ne [Version]'2.0.100.0') { throw "Unverified interface version: $assemblyName. Review compatibility before changing this gate." }
}
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
$common = @('/nologo','/utf8output','/codepage:65001','/langversion:5','/warn:4','/warnaserror+','/optimize+','/platform:anycpu',('/reference:' + (Join-Path $frameworkDir 'netstandard.dll')),'/reference:System.Core.dll','/reference:System.Xml.Linq.dll','/reference:System.Xml.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll')
foreach ($name in $refs) { $common += '/reference:' + (Join-Path $ParatextDir $name) }
& $compilerPath @common /target:library ("/out:" + (Join-Path $outputDir 'ParatextNoteSuggestions.dll')) @sources
if ($LASTEXITCODE -ne 0) { throw 'Plugin compilation failed.' }
Copy-Item -LiteralPath (Join-Path $outputDir 'ParatextNoteSuggestions.dll') -Destination (Join-Path $outputDir 'ParatextNoteSuggestions.ptxplg') -Force
# Runtime interfaces are copied only into ignored local test output, never into the release package.
foreach ($name in $refs) { Copy-Item -LiteralPath (Join-Path $ParatextDir $name) -Destination (Join-Path $outputDir $name) -Force }
if (!$SkipTests) {
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
    & $compilerPath @common /target:exe /main:NoteSuggestionsTests.Program ("/reference:" + (Join-Path $outputDir 'ParatextNoteSuggestions.dll')) ("/out:" + (Join-Path $outputDir 'NoteSuggestions.Tests.exe')) @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $outputDir 'NoteSuggestions.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Validation failed.' }
    & $compilerPath @common /target:winexe /main:NoteSuggestionsTests.DesktopDemo ("/reference:" + (Join-Path $outputDir 'ParatextNoteSuggestions.dll')) ("/out:" + (Join-Path $outputDir 'NoteSuggestions.Demo.exe')) @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Demo compilation failed.' }
    & $compilerPath @common /target:winexe /main:NoteSuggestionsTests.DesktopMenuDemo ("/reference:" + (Join-Path $outputDir 'ParatextNoteSuggestions.dll')) ("/out:" + (Join-Path $outputDir 'NoteSuggestions.MenuDemo.exe')) @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Menu demo compilation failed.' }
}
Write-Output "Built: $(Join-Path $outputDir 'ParatextNoteSuggestions.ptxplg')"

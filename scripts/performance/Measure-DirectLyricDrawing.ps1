param(
    [Parameter(Mandatory)][string]$BaselineDirectory,
    [string]$CurrentDirectory = 'HyPlayer.Playback.Tests/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$baselinePath = (Resolve-Path $BaselineDirectory).Path
$currentPath = (Resolve-Path $CurrentDirectory).Path
$outputPath = Join-Path $repoRoot 'artifacts/performance/lyrics-direct'
New-Item -ItemType Directory -Force $outputPath | Out-Null

# BaselineDirectory must contain the managed b649fb4c HyPlayer.dll and its dependencies,
# captured before changing the checkout. The product is built with VS/MSBuild separately.
foreach ($variant in @('before', 'after')) {
    $probePath = Join-Path $outputPath $variant
    New-Item -ItemType Directory -Force $probePath | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LyricDrawingProbe.cs') -Destination (Join-Path $probePath 'Program.cs')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'HyPlayer.Playback.Tests/Win2DTestHost.cs') -Destination $probePath
    $assemblyPath = if ($variant -eq 'before') { $baselinePath } else { $currentPath }
    $references = foreach ($name in @('HyPlayer', 'Microsoft.Graphics.Canvas.Interop', 'ALRC.Abstraction', 'WinRT.Runtime')) {
        $filePath = if ($name -eq 'HyPlayer') { Join-Path $assemblyPath "$name.dll" } else { Join-Path $currentPath "$name.dll" }
        $escapedPath = [System.Security.SecurityElement]::Escape($filePath)
        "<Reference Include=`"$name`"><HintPath>$escapedPath</HintPath></Reference>"
    }
    if ($variant -eq 'before') {
        $escapedPath = [System.Security.SecurityElement]::Escape((Join-Path $baselinePath 'HyPlayer.LyricEffects.dll'))
        $references += "<Reference Include=`"HyPlayer.LyricEffects`"><HintPath>$escapedPath</HintPath></Reference>"
    }
    $constant = if ($variant -eq 'before') { 'BEFORE' } else { 'AFTER' }
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <WindowsSdkPackageVersion>10.0.26100.87</WindowsSdkPackageVersion><UseUwp>true</UseUwp>
    <OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier><PlatformTarget>x64</PlatformTarget><DefineConstants>$constant</DefineConstants>
  </PropertyGroup>
  <ItemGroup>$($references -join "`n")</ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $probePath 'Probe.csproj')
    dotnet build (Join-Path $probePath 'Probe.csproj') -c Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
    $probeOutput = Join-Path $probePath 'bin/Release/net10.0-windows10.0.26100.0/win-x64'
    Get-ChildItem -LiteralPath $currentPath -Filter '*.dll' | Where-Object Name -ne 'HyPlayer.dll' | Copy-Item -Destination $probeOutput
    if ($variant -eq 'before') {
        Get-ChildItem -LiteralPath $baselinePath -Filter '*.dll' | Copy-Item -Destination $probeOutput
    }
}

foreach ($case in @(@('SoftDepth','words'), @('SoftDepth','long'), @('SoftDepth','sublines'), @('SoftDepth','scroll'), @('Clear','words'), @('Layered','words'), @('None','words'))) {
    for ($round = 0; $round -lt 5; $round++) {
        $order = if ($round % 2 -eq 0) { @('before','after') } else { @('after','before') }
        foreach ($variant in $order) {
            $probe = Join-Path $outputPath "$variant/bin/Release/net10.0-windows10.0.26100.0/win-x64/Probe.dll"
            dotnet $probe $case[0] $case[1] 1 | Set-Content -LiteralPath (Join-Path $outputPath "$variant-$($case[0])-$($case[1])-$round.csv")
            if ($LASTEXITCODE -ne 0) { throw 'Drawing probe failed' }
        }
    }
}
@(
    'Baseline=b649fb4c', "Current=$(git rev-parse HEAD)", "BaselineDirectory=$baselinePath",
    [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription,
    "CPU=$((Get-CimInstance Win32_Processor).Name)", (powercfg /getactivescheme),
    '800x600 at 96 DPI; 300 warmup frames, 200 measured frames per process, five alternating process pairs.',
    'median/P95 measure CPU command submission. Pixel readback drains GPU after each round.',
    'Private bytes are process memory, not GPU VRAM; compositor pacing and VRAM are not measured.'
) | Set-Content -LiteralPath (Join-Path $outputPath 'environment.txt')

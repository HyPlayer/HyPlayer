$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$renderer = Get-Content -Raw (Join-Path $repoRoot 'HyPlayer/LyricRenderer/Text/FocusedLyricTextRenderer.cs')
$start = $renderer.IndexOf('internal static RectangleMaskPlan CreateRectangleMaskPlan(')
$end = $renderer.IndexOf('private static Color MaskColor', $start)
$method = $renderer.Substring($start, $end - $start)
$start = $renderer.IndexOf('internal readonly record struct RectangleMaskPlan(')
$end = $renderer.IndexOf(';', $start) + 1
$plan = $renderer.Substring($start, $end - $start)
$tests = Get-Content -Raw (Join-Path $repoRoot 'HyPlayer.Playback.Tests/FocusedVectorRevealTests.cs')
$testMethods = [regex]::Matches($tests, '(?s)    public void (\w+)\(\).*?(?=    \[Test\]|\z)') |
    Where-Object { $_.Value.Contains('CreateRectangleMaskPlan(') }
$testCode = ($testMethods | ForEach-Object { $_.Value.Replace('FocusedLyricTextRenderer.', '') }) -join "`n"
$timeline = Get-Content -Raw (Join-Path $repoRoot 'HyPlayer/LyricRenderer/Text/FocusedLineRevealTimeline.cs')
Add-Type -TypeDefinition @"
$timeline
public class LyricTailRevealProbe {
    $method
    $plan
    $testCode
}
"@
$probe = [HyPlayer.LyricRenderer.Text.LyricTailRevealProbe]::new()
foreach ($testMethod in $testMethods) {
    $name = $testMethod.Groups[1].Value
    $probe.$name()
    Write-Host "PASS: $name"
}

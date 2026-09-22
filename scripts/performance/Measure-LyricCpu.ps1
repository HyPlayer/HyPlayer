param([string]$Baseline = 'd261730f7931c08ba53646226cf99f643f60f947')
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')
$output = 'artifacts/performance/lyrics-cpu'
New-Item -ItemType Directory -Force $output | Out-Null
function Extract-Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method: $signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    return $source.Substring($start, $end - $start)
}
$path = 'HyPlayer/LyricRenderer/Text/LyricGlyphPlanCollector.cs'
$old = (git show "${Baseline}:$path") -join "`n"
$new = Get-Content -Raw $path
$signature = 'private static int FindNextGlyphStart('
$oldMethod = (Extract-Method $old $signature).Replace('private static int FindNextGlyphStart', 'public static int Before')
$newMethod = (Extract-Method $new 'internal static int FindNextGlyphStart(').Replace('internal static int FindNextGlyphStart', 'public static int After')
$source = @"
using System;
using System.Collections.Generic;
using System.Diagnostics;
public static class LyricCpuProbe {
    $oldMethod
    $newMethod
    static long sink;
    public static void Run() {
        var random = new Random(42);
        for (int test = 0; test < 10000; test++) {
            int n = random.Next(1, 150);
            var map = new int[n];
            for (int i = 0; i < n; i++) map[i] = random.Next(n);
            int start = random.Next(n + 1), glyph = random.Next(n);
            if (Before(map, start, glyph, n) != After(map, start, glyph, n))
                throw new Exception("Cluster boundary changed");
        }
        Console.WriteLine("PASS: 10000 seeded cluster maps (including repeated and nonmonotone mappings)");
        Console.WriteLine("glyphs,variant,round,ns_per_layout,bytes_per_layout");
        foreach (int n in new[] {24, 80, 256}) {
            var map = new int[n];
            for (int i = 0; i < n; i++) map[i] = i;
            Measure(map, false, 5000); Measure(map, true, 5000);
            for (int round = 0; round < 9; round++) {
                bool first = round % 2 == 0;
                foreach (bool after in new[] {first, !first}) {
                    var sample = Measure(map, after, 20000);
                    Console.WriteLine($"{n},{(after ? "after" : "before")},{round},{sample.Item1:F2},{sample.Item2:F2}");
                }
            }
        }
    }
    static (double, double) Measure(int[] map, bool after, int count) {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp(), result = 0;
        for (int repeat = 0; repeat < count; repeat++)
            for (int i = 0; i < map.Length; i++)
                result += after ? After(map, i + 1, i, map.Length) : Before(map, i + 1, i, map.Length);
        long end = Stopwatch.GetTimestamp();
        sink = result;
        return ((end - start) * 1e9 / Stopwatch.Frequency / count,
            (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)count);
    }
}
"@
$source | Set-Content "$output/probe.cs"
@("Baseline=$Baseline", "Current=$(git rev-parse HEAD)", "PowerShell=$($PSVersionTable.PSVersion)",
    [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription,
    [System.Runtime.InteropServices.RuntimeInformation]::OSDescription,
    "CPU=$((Get-CimInstance Win32_Processor).Name)", (powercfg /getactivescheme),
    'Component probe: optimized JIT, no Win2D/GPU; 5000 warmup layouts, 9 alternating rounds of 20000.',
    'Run in a fresh pwsh process; not an end-to-end frame-rate measurement.',
    (git status --short)) | Set-Content "$output/environment.txt"
Add-Type -TypeDefinition $source -CompilerOptions '/optimize+'
[LyricCpuProbe]::Run()

# Lyrics CPU investigation — 2026-09-21

Baseline: `d261730f7931c08ba53646226cf99f643f60f947`. Existing working-tree changes
to `DevelopDoc/capture.json` and `Win2DLyricTextLayouter.cs` were retained.

Host: AMD Ryzen AI 9 HX 370, Windows 10.0.26220, balanced power plan,
PowerShell 7.6.6 / .NET 10.0.12 x64; solution SDK 10.0.302, VS 18 MSBuild.
No HyPlayer process was running. This investigation measures managed CPU
components, not whole-app GPU/display performance or NativeAOT throughput.

## Final controlled component runs

Command, twice in fresh processes after solution compilation finished:

```powershell
pwsh -NoProfile -File scripts/performance/Measure-LyricCpu.ps1 > artifacts/performance/lyrics-cpu/final-1.csv
pwsh -NoProfile -File scripts/performance/Measure-LyricCpu.ps1 > artifacts/performance/lyrics-cpu/final-2.csv
```

Both variants execute in each process, alternating order. Aggregated 18 samples
per variant, nanoseconds for all glyph-boundary lookups in one run:

| Glyphs | Before median (min–max) | After median (min–max) |
| --- | --- | --- |
| 24 | 81.52 (78.64–92.77) | 21.19 (19.17–28.25) |
| 80 | 1036.62 (985.38–1246.48) | 80.33 (71.17–124.62) |
| 256 | 10789.35 (10586.61–11845.30) | 242.25 (213.84–544.10) |

Both variants allocated zero measured bytes in this loop. Short-run timing has
visible tails; no profiler was attached. Earlier exploratory results are retained
but excluded because one repeat overlapped solution compilation. Raw files,
compiled probe source and environment metadata are in
`artifacts/performance/lyrics-cpu/` (ignored by Git).

The supported cause is unnecessary suffix traversal in glyph-boundary lookup.
This common contiguous-map workload now performs N-1 reads rather than N(N-1)/2.
It does not include shaping, rasterization, uploads, masks, or draw submission.
The separate unused focused-frame removal is established by the callee's lack of
any use of that argument; no numerical overall CPU or allocation-rate claim is
made for it.

## Validation

- 10,000 deterministic differential maps pass in each probe invocation.
- `LyricGlyphBoundaryTests`: 2/2 pass, including an exact linear-read budget.
- `Focused*` playback tests: 23/23 pass.
- `git diff --check`: pass.
- Whole solution Release/x64 verification used Visual Studio MSBuild with
  `/p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never
  /p:AppxPackageSigningEnabled=false`. App compilation and NativeAOT linking
  completed. Packaging failed with existing PRI175/PRI277 duplicate
  `Files/HyPlayer.Frieren/Themes/Generic.xbf`; the whole solution is not green.

Tests were run with the built `HyPlayer.Playback.Tests.exe`, using
`--treenode-filter '/*/*/LyricGlyphBoundaryTests/*' --minimum-expected-tests 2`
and `--treenode-filter '/*/*/Focused*/*' --minimum-expected-tests 1`.
Logs are retained beside the benchmark artifacts.

GPU/device-loss/shutdown behavior was not exercised. These changes introduce no
new GPU resources or caches and leave their lifetime handling unchanged. Actual
frame pacing and dominant whole-app costs remain unmeasured.

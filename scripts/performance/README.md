# Lyric CPU component probe

Run from a fresh PowerShell 7 process on Windows:

```powershell
pwsh -NoProfile -File scripts/performance/Measure-LyricCpu.ps1 > artifacts/performance/lyrics-cpu/results.csv
```

Create the output directory before shell redirection on a fresh checkout:

```powershell
New-Item -ItemType Directory -Force artifacts/performance/lyrics-cpu
```

The probe extracts the actual glyph-boundary method from the baseline commit and
working tree, compiles both with optimization enabled, and compares 10,000 seeded
maps before measuring. Repeated/nonmonotone maps cover cluster boundary semantics;
the timed inputs model 24, 80, and 256 adjacent CJK glyphs. Each variant receives
5,000 warmup layouts and nine alternating rounds of 20,000 layouts. Input setup is
outside the measurement; results are consumed and allocation is measured on the
current thread. Record median, minimum, and maximum across rounds, rather than a
single fastest result. `probe.cs` and `environment.txt` preserve the source and host
metadata. Keep the generated artifacts out of Git.

This is an optimized JIT CPU component measurement, not a UWP/NativeAOT/Win2D or
GPU benchmark. Do not translate the speedup into a frame-rate claim. Stop builds
and other heavy workloads before collecting comparable measurements.

## Changes under investigation

- Glyph boundary collection used to scan the entire remaining run even after
  finding the next adjacent glyph index. No closer integer index can exist, so
  terminating here preserves the result for unordered maps as well. A contiguous
  run now needs N-1 map reads rather than N(N-1)/2. This affects layout creation,
  font changes, and resizing, not every warmed rendering frame.
- Focused rendering never consumed its `TextRenderFrame` argument. Removing its
  construction eliminates the resolver's token scans and one frame object per
  active focused line per frame. The legacy rendering branch still resolves its
  frame normally. No GPU resources, reveal math, or effect order are changed.

`LyricGlyphBoundaryTests` checks both the exact read-count budget and equivalence
against a full-scan oracle. Existing focused reveal/timeline tests cover rendering
math. The lyric JSON path already uses `LyricEffectJsonContext`; this change does
not require a new generator or reflection-based serialization.

Remaining candidates require separate measurement: wheel-event `IndexOf` scans,
per-glyph profile dispatch, native glyph resource creation, feathered masks, and
GPU/compositor frame pacing. No dominant whole-application bottleneck has been
established by this component probe.

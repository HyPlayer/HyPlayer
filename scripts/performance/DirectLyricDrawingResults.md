# Direct lyric drawing: Windows x64 verification

Baseline: `b649fb4c`; candidate: local direct-drawing refactor, 2026-10-12.
Both builds select SoftDepth + SoftLift for the default scenarios. Other line presets retain SoftLift.

Actual Win2D CanvasRenderTarget drawing, 800x600 at 96 DPI, five lines (nine for sublines).
Each variant uses a fresh process, 300 warmup frames and 200 measured frames; five process
pairs alternate execution order. Readback drains the GPU after each measurement block.
The values below are medians across those five runs, before / after.

| Scenario | CPU submission median ms | CPU submission P95 ms | Managed bytes/frame | Process CPU ms/200 frames |
| --- | ---: | ---: | ---: | ---: |
| SoftDepth-words | 1.908 / 0.887 | 3.200 / 2.568 | 19156 / 977 | 734 / 281 |
| SoftDepth-long | 18.418 / 1.187 | 27.002 / 2.969 | 146044 / 3359 | 4938 / 359 |
| SoftDepth-sublines | 2.700 / 0.846 | 5.064 / 2.011 | 37304 / 1183 | 969 / 234 |
| SoftDepth-scroll | 2.644 / 0.743 | 4.000 / 1.672 | 36574 / 977 | 922 / 250 |
| Clear-words | 1.952 / 0.778 | 3.491 / 1.815 | 19143 / 977 | 641 / 219 |
| Layered-words | 1.921 / 0.823 | 3.581 / 1.821 | 19143 / 977 | 703 / 250 |
| None-words | 1.011 / 0.552 | 1.642 / 1.630 | 18335 / 3383 | 281 / 141 |

The numeric progress/lift/timeline/transition loop allocates **0 managed bytes** after warmup.
The full draw still allocates WinRT wrapper/session objects; it is not claimed to allocate zero.
Tests assert at most one transient content CommandList per active line, and no transient content
CommandList for cached inactive lines. Glyph-level effect images and masks are removed.

These are CPU submission measurements, not compositor presentation times or GPU execution
duration. Private process memory varies with warmup/GC and is not a VRAM measurement.
A nine-line scenario simulates concurrent lines; it is not a full application/ALRC ingestion trace.
No whole-app frame-rate or GPU-memory claim is made. Actual-device compositor traces and
interactive settings/dialog lifecycle checks remain separate validation work.

Rendering tests exercise every retained preset and reveal mode, shaped mixed-direction text,
translation, transforms, cached idle drawing and perspective. A pixel test verifies a ten-character
CJK line retains its pending tail and displays translation at 70% progress. Existing geometry,
timeline, glyph-boundary, lift and playback tests are retained. Source-generated numeric settings
replace the old hylfx path; legacy files are not read, modified or deleted.

Reproduce using `Measure-DirectLyricDrawing.ps1`; keep the baseline managed app assembly
and dependencies before rebuilding the solution. Raw CSV and PNGs are under
`artifacts/performance/lyrics-direct` (ignored build/measurement output).

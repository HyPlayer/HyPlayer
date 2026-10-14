# Stretch component benchmark

This diagnostic executable links the actual managed DSP sources, without the UWP shell.
It is not part of the app or solution packaging. It does not require a native DLL unless
`--native <path>` is explicitly supplied for reference comparisons.

Publish with Native AOT on Windows, using the installed Visual Studio C++ tools for the
.NET AOT linker (the application no longer builds a custom C++ project):

```powershell
dotnet publish tools/StretchBenchmarks/StretchBenchmarks.csproj -c Release -r win-x64 -p:PublishAot=true -o <artifact-directory>
<artifact-directory>/StretchBenchmarks.exe
<artifact-directory>/StretchBenchmarks.exe --native <reference-hyp_stretch.dll>
<artifact-directory>/StretchBenchmarks.exe --compare --native <reference-hyp_stretch.dll>
<artifact-directory>/StretchBenchmarks.exe --memory
<artifact-directory>/StretchBenchmarks.exe --stream
```

The reference DLL must come from the original 43dfc936 version. Keep it outside the
repository and application package. Record its SHA256 with results. Comparison mode
reports waveform SNR and maximum difference for 120 sample-rate/preset/speed/pitch
combinations. Floating-point phase prediction is sensitive to operation ordering;
this is diagnostic evidence, not a promise of bit-identical audio or a listening test.

Timing mode measures one and two stereo workers at 44.1/48/96/192 kHz, with upstream
default and compact presets. Each worker uses 10 ms audio batches at 1.25x playback,
128 warmup batches and three rounds of 300 measured batches. Input and measurement
storage are allocated before the measurement. CSV includes mean, p50, p99, maximum and
thread allocation; processing allocation is required to remain zero. Native comparison
uses identical PCM and batch sizes. Profile separately with `--profile` (30-second
192 kHz dual-worker load) to avoid profiler overhead in clean timings.

Acceptance: latency must match the reference and steady-state allocation is zero.
Mean processing must fit the 10 ms audio-batch budget; the additional p99 target is
10 ms to preserve headroom. Report every target miss and maximum, not just averages:
the default preset computes spectra in bursts every 30 ms. The existing MixPump
buffers 350 ms (100 ms low water), so a batch tail above 10 ms is a headroom warning,
not direct proof of an audio underrun. Report the native
slowdown, cold-start costs, worst-case outliers, host load and hardware. This component
budget does not guarantee application-level audio underrun immunity. Use Playback
tests for reconstruction, pitch, stereo, chunk continuity, silence, reset and lifetime.

Memory mode warms immutable tables, measures the first and second mutable instance,
uses full-collection live-byte measurements with both wrappers alive, then checks
disposed-wrapper retention and 32 create/dispose cycles. Report the bounded shared
table cache separately from per-instance working memory. This is managed allocation/
live-object evidence, not an RSS or native-heap measurement or application cold-start test.

Stream mode measures the full SpeedProcessor path (PCM append/retention, stretching or
resampling, gain and limiter) at 48/192 kHz with 250 ms of queued input. It covers unity,
1.25x/0.75x pitch preservation and 2x resampling, using 128 warmup blocks followed by
three rounds of 300 blocks. The CSV records mean/p99/max, zero-allocation checks and an
output checksum. Run both binaries in reverse order again to expose host drift. Playback
tests separately compare every sample between fully buffered and incremental input across
compaction, growth, path changes, end padding and reset. Buffer capacity is unchanged.

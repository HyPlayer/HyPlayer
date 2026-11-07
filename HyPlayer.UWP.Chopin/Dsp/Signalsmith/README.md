# Managed Signalsmith stretch

This directory ports the HyPlayer-exposed operations of Signalsmith Stretch 1.3.2
and Signalsmith Linear to C#. The upstream MIT license is retained in LICENSE.txt.
The original sources are recorded in commit 43dfc936 under HyPlayer.Native/third_party.

The port retains the modified (half-bin) STFT, Kaiser/perfect-reconstruction window,
input/output latency and pre-roll, spectral energy and peak mapping, phase prediction,
cross-channel phase locking, extreme-stretch phase randomisation, and silence handling.
The FFT backend uses precomputed mixed-radix plans for the same upstream fast sizes,
half-size real transforms, in-place/SIMD radix-2 butterflies and specialised radix-3/5
combination. SIMD has a scalar fallback; no platform-native library is loaded.
Only the API formerly exported by stretch_api.cpp is supported: constructor presets,
reset, output seek, processing and transpose. Unexposed custom frequency/formant maps,
split-computation mode, flush and exact-buffer helpers are intentionally omitted.

Mutable transform, spectrum, history and overlap buffers are allocated at construction.
The optional peak/pitch-mapping workspace is reserved on the first non-unity SetTranspose,
before Process; plain time stretching does not pay for unused pitch mapping. Process
and OutputSeek do not resize buffers or allocate. Each instance belongs to one
audio worker; callers must synchronise speed changes, reset and disposal as before.
Floating-point operation ordering and the random generator differ from the C++ backend,
so sample-for-sample identity is not promised. Native reference comparison, spectral,
stereo, latency, chunk-boundary, allocation and AOT performance checks form acceptance.

TimeStretcher keeps its existing public contract and capability checks. No DLL discovery,
C++ toolchain, runtime code generation or reflection is required by this implementation.

Immutable FFT tables and normalised windows are shared. Both caches are LRU with at
most eight entries: estimated retained table storage is capped at 2 MiB for FFT plans
and 512 KiB for windows (small cache bookkeeping is additional). Oversized custom
plans are not cached. Active instances own references safely across eviction, and their
PCM/phase/scratch buffers are never shared. Cache locks occur only at construction or
latency-only queries, never in Process, OutputSeek or steady-state rendering. Dispose
drops the processor reference so retained wrappers do not retain large working arrays.
SpeedProcessor queries latency from the window plan without allocating a full worker.
Unity pitch reuses the existing input spectrum instead of storing a duplicate complex
prediction array; only previous prediction energy is retained. Non-unity pitch owns
its interpolation storage. Pure power-of-two FFTs allocate neither an outer-DFT scratch
buffer nor an unused retained outer-root table. No global pool retains mutable PCM.

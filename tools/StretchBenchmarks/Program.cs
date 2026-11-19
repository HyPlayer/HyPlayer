using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HyPlayer.UWP.Chopin.Dsp;

// Diagnostic-only native reference access. Neither this tool nor a reference DLL is shipped.
internal static class Program
{
    private static int Main(string[] args)
    {
        var nativeIndex = Array.IndexOf(args, "--native");
        var nativePath = nativeIndex >= 0 ? args[nativeIndex + 1] : null;
        Console.WriteLine($"runtime={RuntimeInformation.FrameworkDescription} arch={RuntimeInformation.ProcessArchitecture} os={RuntimeInformation.OSDescription} dynamicCode={RuntimeFeature.IsDynamicCodeSupported}");
        if (args.Contains("--memory")) { MeasureMemory(); return 0; }
        if (args.Contains("--stream")) { StreamBenchmark.Run(); return 0; }
        if (args.Contains("--profile"))
        {
            using var worker = new Worker(192000, false, 2, 1.25, null);
            var until = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
            while (Stopwatch.GetTimestamp() < until) worker.Process();
            return 0;
        }
        if (args.Contains("--compare"))
        {
            if (nativePath is null) throw new ArgumentException("--compare requires --native <reference DLL>.");
            return Compare(nativePath);
        }
        Console.WriteLine("rate,compact,voices,backend,round,mean_ms,p50_ms,p99_ms,max_ms,allocated_bytes,budget_ms");
        foreach (var rate in new[] { 44100, 48000, 96000, 192000 })
            foreach (var compact in new[] { false, true })
                foreach (var voices in new[] { 1, 2 })
                {
                    Measure(rate, compact, voices, null);
                    if (nativePath is not null) Measure(rate, compact, voices, nativePath);
                }
        return 0;
    }

    private static void Measure(int rate, bool compact, int voices, string nativePath)
    {
        using var worker = new Worker(rate, compact, voices, 1.25, nativePath);
        // Warm measurement helpers too: their first-use AOT initialisation is not DSP allocation.
        for (var warm = 0; warm < 128; warm++)
        {
            var start = Stopwatch.GetTimestamp();
            worker.Process();
            _ = Stopwatch.GetElapsedTime(start);
        }
        _ = GC.GetAllocatedBytesForCurrentThread();
        var durations = new double[300];
        for (var round = 0; round < 3; round++)
        {
            double totalMilliseconds = 0;
            var allocation = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < durations.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                worker.Process();
                durations[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                totalMilliseconds += durations[i];
            }
            allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
            Array.Sort(durations);
            Console.WriteLine(FormattableString.Invariant($"{rate},{compact},{voices},{(nativePath is null ? "managed" : "native")},{round},{totalMilliseconds / durations.Length:F5},{durations[150]:F5},{durations[297]:F5},{durations[^1]:F5},{allocation},10"));
            if (allocation != 0) throw new InvalidOperationException("Steady-state DSP allocated managed memory.");
        }
    }

    private static TimeStretcher Create(int rate, bool compact) => compact
        ? new TimeStretcher(2, rate, 0.07, 0.0175) : new TimeStretcher(2, rate);

    private static void MeasureMemory()
    {
        Console.WriteLine("rate,compact,first_instance_bytes,second_instance_bytes,two_live_bytes,disposed_live_bytes,churn_32_bytes,churn_32_ms");
        _ = GC.GetAllocatedBytesForCurrentThread();
        _ = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp());
        foreach (var rate in new[] { 44100, 48000, 96000, 192000 })
            foreach (var compact in new[] { false, true })
            {
                // Warm type initialisers and immutable plans; measure fresh mutable instances separately.
                Create(rate, compact).Dispose();
                var baseline = GC.GetTotalMemory(true);
                var startBytes = GC.GetAllocatedBytesForCurrentThread();
                var first = Create(rate, compact);
                var firstBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
                startBytes = GC.GetAllocatedBytesForCurrentThread();
                var second = Create(rate, compact);
                var secondBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
                var live = GC.GetTotalMemory(true) - baseline;
                first.Dispose(); second.Dispose();
                var retained = GC.GetTotalMemory(true) - baseline;
                GC.KeepAlive(first); GC.KeepAlive(second);
                startBytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < 32; i++) Create(rate, compact).Dispose();
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var churn = GC.GetAllocatedBytesForCurrentThread() - startBytes;
                Console.WriteLine(FormattableString.Invariant($"{rate},{compact},{firstBytes},{secondBytes},{live},{retained},{churn},{elapsed:F3}"));
            }
    }

    private static int Compare(string path)
    {
        Console.WriteLine("rate,compact,speed,pitch,seek_frames,snr_db,max_error");
        foreach (var rate in new[] { 44100, 48000, 96000, 192000 })
            foreach (var compact in new[] { false, true })
                foreach (var speed in new[] { 0.5, 0.75, 1, 1.25, 2 })
                    foreach (var pitch in new[] { 1d, 0.8, 1.2 })
                    {
                        using var managed = new Worker(rate, compact, 1, speed, null, pitch);
                        using var native = new Worker(rate, compact, 1, speed, path, pitch);
                        if (managed.SeekFrames != native.SeekFrames) throw new InvalidOperationException("Pre-roll latency differs from the native reference.");
                        double signal = 0, error = 0, maximum = 0;
                        for (var i = 0; i < 50; i++)
                        {
                            managed.Process(); native.Process();
                            var a = managed.Output; var b = native.Output;
                            for (var s = 0; s < a.Length; s++)
                            {
                                if (!float.IsFinite(a[s])) throw new InvalidOperationException("Non-finite output.");
                                var difference = (double)a[s] - b[s];
                                signal += b[s] * b[s]; error += difference * difference;
                                maximum = Math.Max(maximum, Math.Abs(difference));
                            }
                        }
                        var snr = 10 * Math.Log10(signal / Math.Max(error, 1e-30));
                        Console.WriteLine(FormattableString.Invariant($"{rate},{compact},{speed},{pitch},{managed.SeekFrames},{snr:F3},{maximum:G6}"));
                    }
        return 0;
    }

    private sealed class Worker : IDisposable
    {
        private readonly TimeStretcher[] _managed;
        private readonly NativeReference _native;
        private readonly float[] _input;
        private readonly int _quantum;
        private readonly double _speed;
        private int _position;
        private double _carry;
        public readonly float[] Output;
        public readonly int SeekFrames;

        public Worker(int rate, bool compact, int voices, double speed, string nativePath, double pitch = 1)
        {
            _quantum = rate / 100; _speed = speed;
            _input = Fixture(rate, rate * 6);
            Output = new float[_quantum * 2];
            if (nativePath is not null)
            {
                _native = new NativeReference(nativePath, rate, compact, voices, pitch);
                SeekFrames = _native.SeekLength(speed);
                _native.Seek(_input.AsSpan(0, SeekFrames * 2));
            }
            else
            {
                _managed = new TimeStretcher[voices];
                for (var i = 0; i < voices; i++)
                {
                    _managed[i] = compact ? new TimeStretcher(2, rate, 0.07, 0.0175) : new TimeStretcher(2, rate);
                    _managed[i].SetTranspose(pitch);
                    SeekFrames = _managed[i].OutputSeekLength(speed);
                    _managed[i].OutputSeek(_input.AsSpan(0, SeekFrames * 2));
                }
            }
            _position = SeekFrames;
        }
        public void Process()
        {
            var wanted = _speed * _quantum + _carry;
            var take = (int)Math.Floor(wanted); _carry = wanted - take;
            if (_position + take >= _input.Length / 2) _position = 0;
            var input = _input.AsSpan(_position * 2, take * 2);
            if (_native is not null) _native.Process(input, Output);
            else for (var i = 0; i < _managed.Length; i++) _managed[i].Process(input, Output);
            _position += take;
        }
        public void Dispose()
        {
            _native?.Dispose();
            if (_managed is not null) foreach (var stretch in _managed) stretch.Dispose();
        }
        private static float[] Fixture(int rate, int frames)
        {
            var input = new float[frames * 2];
            for (var i = 0; i < frames; i++)
            {
                var t = i / (double)rate;
                input[2 * i] = (float)(0.2 * Math.Sin(2 * Math.PI * 440 * t) + 0.1 * Math.Sin(2 * Math.PI * 1207 * t) + (i % 2003 == 0 ? 0.3 : 0));
                input[2 * i + 1] = (float)(0.17 * Math.Sin(2 * Math.PI * 440 * t + 0.3) + 0.12 * Math.Sin(2 * Math.PI * 1733 * t));
            }
            return input;
        }
    }

    private sealed unsafe class NativeReference : IDisposable
    {
        private readonly nint _library;
        private readonly nint[] _handles;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _destroy;
        private readonly delegate* unmanaged[Cdecl]<nint, float, int> _length;
        private readonly delegate* unmanaged[Cdecl]<nint, float*, int, void> _seek;
        private readonly delegate* unmanaged[Cdecl]<nint, float*, int, float*, int, void> _process;
        public NativeReference(string path, int rate, bool compact, int voices, double pitch)
        {
            _library = NativeLibrary.Load(path);
            var create = (delegate* unmanaged[Cdecl]<int, float, int, nint>)NativeLibrary.GetExport(_library, "hyp_stretch_create");
            var sized = (delegate* unmanaged[Cdecl]<int, int, int, nint>)NativeLibrary.GetExport(_library, "hyp_stretch_create_sized");
            var transpose = (delegate* unmanaged[Cdecl]<nint, float, void>)NativeLibrary.GetExport(_library, "hyp_stretch_set_transpose");
            _destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(_library, "hyp_stretch_destroy");
            _length = (delegate* unmanaged[Cdecl]<nint, float, int>)NativeLibrary.GetExport(_library, "hyp_stretch_output_seek_length");
            _seek = (delegate* unmanaged[Cdecl]<nint, float*, int, void>)NativeLibrary.GetExport(_library, "hyp_stretch_output_seek");
            _process = (delegate* unmanaged[Cdecl]<nint, float*, int, float*, int, void>)NativeLibrary.GetExport(_library, "hyp_stretch_process");
            _handles = new nint[voices];
            for (var i = 0; i < voices; i++)
            {
                _handles[i] = compact ? sized(2, (int)(rate * 0.07), (int)(rate * 0.0175)) : create(2, rate, 0);
                transpose(_handles[i], (float)pitch);
            }
        }
        public int SeekLength(double speed) => _length(_handles[0], (float)speed);
        public void Seek(ReadOnlySpan<float> input)
        {
            fixed (float* p = input) foreach (var handle in _handles) _seek(handle, p, input.Length / 2);
        }
        public void Process(ReadOnlySpan<float> input, Span<float> output)
        {
            fixed (float* p = input) fixed (float* q = output)
                foreach (var handle in _handles) _process(handle, p, input.Length / 2, q, output.Length / 2);
        }
        public void Dispose()
        {
            foreach (var handle in _handles) _destroy(handle);
            NativeLibrary.Free(_library);
        }
    }
}

using System.Diagnostics;
using HyPlayer.UWP.Chopin.Dsp;

internal static class StreamBenchmark
{
    public static void Run()
    {
        Console.WriteLine("rate,speed,preserve,round,mean_ms,p99_ms,max_ms,allocated_bytes,checksum");
        foreach (var rate in new[] { 48000, 192000 })
        foreach (var (speed, preserve) in new[] { (1d, true), (1.25, true), (0.75, true), (2d, false) })
        {
            var block = rate / 100;
            var source = new float[rate * 2];
            for (var i = 0; i < rate; i++)
            {
                source[2 * i] = (float)(0.2 * Math.Sin(2 * Math.PI * 440 * i / rate));
                source[2 * i + 1] = source[2 * i] * 0.5f;
            }
            using var processor = new SpeedProcessor(rate, block, speed, preserve);
            var output = new float[block * 2];
            var durations = new double[300];
            void Process()
            {
                // Keep 250 ms ahead, exercising retained PCM rather than only minimal feeds.
                var required = processor.RequiredInput(block) + rate / 4;
                while (processor.BufferedEnd < required)
                {
                    var offset = (int)(processor.BufferedEnd % rate);
                    var frames = (int)Math.Min(required - processor.BufferedEnd, rate - offset);
                    processor.Append(source.AsSpan(offset * 2, frames * 2));
                }
                if (!processor.Render(output)) throw new InvalidOperationException("Buffered stream starved.");
            }
            for (var i = 0; i < 128; i++)
            {
                var start = Stopwatch.GetTimestamp(); Process();
                _ = Stopwatch.GetElapsedTime(start);
            }
            _ = GC.GetAllocatedBytesForCurrentThread();
            for (var round = 0; round < 3; round++)
            {
                double checksum = 0, total = 0;
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < durations.Length; i++)
                {
                    var start = Stopwatch.GetTimestamp(); Process();
                    total += durations[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    checksum += output[0];
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Array.Sort(durations);
                Console.WriteLine(FormattableString.Invariant($"{rate},{speed},{preserve},{round},{total / durations.Length:F6},{durations[297]:F6},{durations[^1]:F6},{allocated},{checksum:R}"));
                if (allocated != 0) throw new InvalidOperationException("Stream processing allocated.");
            }
        }
    }
}

using HyPlayer.UWP.Chopin.Dsp;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class TimeStretcherTests
{
    [Test]
    [Arguments(44100)]
    [Arguments(48000)]
    [Arguments(96000)]
    [Arguments(192000)]
    public void SeekLatency_ShouldMatchUpstreamPreset(int rate)
    {
        using var stretch = new TimeStretcher(2, rate);
        var block = (int)(rate * 0.12);
        var outputLatency = block / 2;
        Ensure(stretch.OutputSeekLength(0) == block - outputLatency, "Input latency changed.");
        Ensure(stretch.OutputSeekLength(1) == block, "Unity pre-roll length changed.");
        Ensure(stretch.OutputSeekLength(2) == block + outputLatency, "Double-speed pre-roll length changed.");
    }

    [Test]
    public void Unity_ShouldReconstructStereoAndRemainIndependentOfChunkBoundaries()
    {
        const int rate = 48000;
        var input = Tone(rate, rate, 440);
        var small = Render(input, rate, 1, 1, 257, rate / 2);
        var large = Render(input, rate, 1, 1, 2048, rate / 2);
        // Native 43dfc936 reference: OutputSeek initially settles its window normalisation.
        var nativeStart = new float[] { 0, 0.0141092f, 0.0281729f, 0.0421445f, 0.0559778f, 0.0696266f };
        for (var i = 0; i < nativeStart.Length; i++)
            Ensure(Math.Abs(small[2 * i] - nativeStart[i]) < 0.000002f, "Pre-roll startup differs from the native reference.");
        for (var i = 0; i < small.Length; i++)
        {
            if (i >= rate / 20 * 2)
                Ensure(Math.Abs(small[i] - input[i]) < 0.0001f, "Unity transform did not reconstruct its input after pre-roll settling.");
            Ensure(Math.Abs(small[i] - large[i]) < 0.0001f, "Chunk boundaries changed unity output.");
        }
    }

    [Test]
    [Arguments(0.5, 1d)]
    [Arguments(1.25, 1d)]
    [Arguments(2d, 1d)]
    [Arguments(1d, 0.8)]
    [Arguments(1d, 1.2)]
    public void StretchAndTranspose_ShouldRetainExpectedPitchAndStereoPhase(double speed, double transpose)
    {
        const int rate = 48000;
        var input = Tone(rate, rate * 3, 440);
        var output = Render(input, rate, speed, transpose, 480, rate);
        var expected = Correlation(output, rate, 440 * transpose);
        var offPitch = Math.Max(Correlation(output, rate, 440 * transpose - 30), Correlation(output, rate, 440 * transpose + 30));
        Ensure(expected > offPitch * 10, "Pitch shifting/time stretching changed the expected fundamental.");
        double signal = 0, error = 0;
        foreach (var sample in output) Ensure(float.IsFinite(sample), "Non-finite audio output.");
        for (var i = rate / 8; i < output.Length / 2; i++)
        {
            signal += output[2 * i] * output[2 * i];
            var difference = output[2 * i + 1] - output[2 * i] * 0.5;
            error += difference * difference;
        }
        // The native pitch mapper has ~1.2e-8 relative stereo error for this 1.2x fixture.
        Ensure(signal > 1 && error / signal < 1e-7, "Stereo phase/gain relationship was lost.");
    }

    [Test]
    [Arguments(1.25, 1d)]
    [Arguments(0.25, 1d)]
    [Arguments(0.75, 1.2)]
    public void ProcessingAndSeek_ShouldNotAllocateAfterConstruction(double speed, double transpose)
    {
        using var stretch = new TimeStretcher(2, 48000);
        stretch.SetTranspose(transpose);
        var input = Tone(48000, stretch.OutputSeekLength(speed), 440);
        var output = new float[960];
        var chunk = input.AsSpan(0, (int)(480 * speed) * 2);
        stretch.OutputSeek(input);
        for (var i = 0; i < 100; i++) stretch.Process(chunk, output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) stretch.Process(chunk, output);
        stretch.OutputSeek(input);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Ensure(allocated == 0, $"Audio processing allocated {allocated} bytes.");
    }

    [Test]
    public void SilenceAndReset_ShouldClearPriorAudioWithoutInvalidSamples()
    {
        using var stretch = new TimeStretcher(2, 48000);
        var input = Tone(48000, stretch.OutputSeekLength(1), 440);
        var output = new float[960];
        stretch.OutputSeek(input);
        stretch.Process(input.AsSpan(0, output.Length), output);
        stretch.Reset();
        Array.Clear(input);
        for (var i = 0; i < 40; i++) stretch.Process(input.AsSpan(0, output.Length), output);
        foreach (var sample in output) Ensure(sample == 0, "Reset/silence retained old audio.");
        stretch.OutputSeek(input);
        stretch.Process(input.AsSpan(0, output.Length), output);
        foreach (var sample in output) Ensure(sample == 0, "Seeking in silence produced audio.");
    }

    [Test]
    public void SpeedProcessor_ShouldSurviveSpeedChangesAndSeekingWithoutNativeLibrary()
    {
        const int rate = 48000;
        const int quantum = 480;
        using var processor = new SpeedProcessor(rate, quantum);
        var input = Tone(rate, rate * 4, 440);
        var output = new float[quantum * 2];
        var appended = 0;
        foreach (var speed in new[] { 1d, 2d, 0.75, 1.25, 1d })
        {
            processor.SetSpeed(speed, true);
            for (var block = 0; block < 10; block++)
            {
                var required = processor.RequiredInput(quantum);
                if (required > appended)
                {
                    var take = checked((int)required - appended);
                    processor.Append(input.AsSpan(appended * 2, take * 2));
                    appended += take;
                }
                Ensure(processor.Render(output), "A buffered speed change failed to render.");
                foreach (var sample in output) Ensure(float.IsFinite(sample), "Speed change produced non-finite audio.");
            }
        }
        Ensure(processor.OutputFrames == quantum * 50, "Speed changes lost output frames.");
        processor.Reset();
        appended = checked((int)processor.RequiredInput(quantum));
        processor.Append(input.AsSpan(0, appended * 2));
        Ensure(processor.Render(output), "Playback failed after seeking/resetting the managed processor.");
    }

    [Test]
    public void LatencyQuery_ShouldNotCreateAndDiscardAFullProcessor()
    {
        const int rate = 192000;
        // Warm only the immutable tables; the old latency probe still allocated a full worker here.
        using (var warm = new TimeStretcher(2, rate, 0.07, 0.0175)) { }
        using var processor = new SpeedProcessor(rate, 1920, 1.25);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var required = processor.RequiredInput(1920);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Ensure(required > 1920, "Latency query did not include pre-roll.");
        Ensure(allocated == 0, $"Latency-only query allocated {allocated} bytes.");
    }

    [Test]
    [NotInParallel]
    [Arguments(false, 2_000_000L)]
    [Arguments(true, 1_200_000L)]
    public void WarmHighRateInstances_ShouldStayWithinWorkingMemoryBudget(bool compact, long budget)
    {
        static TimeStretcher Create(bool compact) => compact
            ? new TimeStretcher(2, 192000, 0.07, 0.0175) : new TimeStretcher(2, 192000);
        using (var warm = Create(compact)) { }
        var before = GC.GetAllocatedBytesForCurrentThread();
        using var worker = Create(compact);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Ensure(allocated < budget, $"192 kHz worker allocated {allocated} bytes against a {budget}-byte budget.");
    }

    [Test]
    public async Task SharedPlans_ShouldKeepConcurrentWorkersIndependent()
    {
        const int rate = 48000;
        var left = Tone(rate, rate * 2, 440);
        var right = Tone(rate, rate * 2, 880);
        var outputs = await Task.WhenAll(
            Task.Run(() => Render(left, rate, 1.25, 1, 480, rate / 2)),
            Task.Run(() => Render(right, rate, 1.25, 1, 480, rate / 2)));
        Ensure(Correlation(outputs[0], rate, 440) > Correlation(outputs[0], rate, 880) * 100,
            "Another worker contaminated the shared plan's first output.");
        Ensure(Correlation(outputs[1], rate, 880) > Correlation(outputs[1], rate, 440) * 100,
            "Another worker contaminated the shared plan's second output.");
    }

    [Test]
    public void InvalidBuffersAndDisposedInstances_ShouldFailExplicitly()
    {
        using var stretch = new TimeStretcher(2, 48000);
        Expect<ArgumentException>(() => stretch.Process(new float[3], new float[2]));
        var overlap = new float[128];
        Expect<ArgumentException>(() => stretch.Process(overlap, overlap));
        Expect<ArgumentOutOfRangeException>(() => stretch.SetTranspose(double.NaN));
        stretch.Dispose(); stretch.Dispose();
        Expect<ObjectDisposedException>(() => stretch.Reset());
        Expect<ObjectDisposedException>(() => stretch.OutputSeekLength(1));
    }

    private static float[] Render(float[] input, int rate, double speed, double transpose, int quantum, int frames)
    {
        using var stretch = new TimeStretcher(2, rate);
        stretch.SetTranspose(transpose);
        var seek = stretch.OutputSeekLength(speed);
        stretch.OutputSeek(input.AsSpan(0, seek * 2));
        var output = new float[frames * 2];
        var at = seek;
        double carry = 0;
        for (var done = 0; done < frames;)
        {
            var count = Math.Min(quantum, frames - done);
            var wanted = count * speed + carry;
            var take = (int)Math.Floor(wanted); carry = wanted - take;
            stretch.Process(input.AsSpan(at * 2, take * 2), output.AsSpan(done * 2, count * 2));
            done += count; at += take;
        }
        return output;
    }
    private static float[] Tone(int rate, int frames, double frequency)
    {
        var result = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            result[2 * i] = (float)(0.25 * Math.Sin(2 * Math.PI * frequency * i / rate));
            result[2 * i + 1] = result[2 * i] * 0.5f;
        }
        return result;
    }
    private static double Correlation(float[] output, int rate, double frequency)
    {
        double real = 0, imaginary = 0;
        for (var i = rate / 8; i < output.Length / 2; i++)
        {
            var angle = 2 * Math.PI * frequency * i / rate;
            real += output[2 * i] * Math.Cos(angle); imaginary += output[2 * i] * Math.Sin(angle);
        }
        return real * real + imaginary * imaginary;
    }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

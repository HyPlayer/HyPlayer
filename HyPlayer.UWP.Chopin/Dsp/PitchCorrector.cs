using System;
using HyPlayer.UWP.Chopin.Utils;
using Windows.Media;
using Windows.Media.Audio;

namespace HyPlayer.UWP.Chopin.Dsp
{
    internal sealed class PitchCorrector : IDisposable
    {
        private readonly CallbackGate _gate = new();
        private readonly TimeStretcher _stretcher;
        private readonly object _stretchLock = new();
        private float[] _fifo = new float[8192];
        private int _fifoCount;
        private float[] _input = new float[4096];
        private float[] _output = new float[4096];

        internal PitchCorrector(AudioFrameOutputNode capture, AudioFrameInputNode render, int sampleRate)
        {
            Capture = capture;
            Render = render;
            _stretcher = new TimeStretcher(2, sampleRate);
            render.QuantumStarted += OnQuantumStarted;
        }

        internal AudioFrameOutputNode Capture { get; }
        internal AudioFrameInputNode Render { get; }

        internal void SetSpeed(double speed)
        {
            lock (_stretchLock) _stretcher.SetTranspose(1 / Math.Max(0.1, speed));
        }

        private void OnQuantumStarted(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
        {
            if (!_gate.TryEnter()) return;
            try
            {
                Process(sender, args.RequiredSamples);
            }
            catch
            {
            }
            finally
            {
                _gate.Exit();
            }
        }

        private unsafe void Process(AudioFrameInputNode sender, int frames)
        {
            if (frames <= 0) return;
            using (var captured = Capture.GetFrame())
            using (var buffer = captured.LockBuffer(AudioBufferAccessMode.Read))
            using (var reference = buffer.CreateReference())
            {
                reference.GetBuffer(out var bytes, out _);
                var count = (int)(buffer.Length / 4);
                if (_fifoCount + count > _fifo.Length) Array.Resize(ref _fifo, Math.Max(_fifo.Length * 2, _fifoCount + count));
                new ReadOnlySpan<float>((float*)bytes, count).CopyTo(_fifo.AsSpan(_fifoCount));
                _fifoCount += count;
            }

            var samples = frames * 2;
            if (_input.Length < samples) _input = new float[samples];
            if (_output.Length < samples) _output = new float[samples];
            var input = _input.AsSpan(0, samples);
            if (_fifoCount >= samples)
            {
                _fifo.AsSpan(0, samples).CopyTo(input);
                _fifo.AsSpan(samples, _fifoCount - samples).CopyTo(_fifo);
                _fifoCount -= samples;
            }
            else
            {
                input.Clear();
            }
            if (_fifoCount > samples * 8) _fifoCount = 0;

            var output = _output.AsSpan(0, samples);
            lock (_stretchLock) _stretcher.Process(input, output);

            using var frame = new AudioFrame((uint)(samples * 4));
            using (var buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
            using (var reference = buffer.CreateReference())
            {
                reference.GetBuffer(out var bytes, out _);
                output.CopyTo(new Span<float>((float*)bytes, samples));
            }
            sender.AddFrame(frame);
        }

        public void Dispose()
        {
            _gate.CloseAndDrain();
            Render.QuantumStarted -= OnQuantumStarted;
            lock (_stretchLock) _stretcher.Dispose();
        }
    }
}

using System;
using System.Threading;
using HyPlayer.UWP.Chopin.Utils;
using Windows.Media;
using Windows.Media.Audio;

namespace HyPlayer.UWP.Chopin.Dsp
{
    internal sealed class PitchCorrector : IDisposable
    {
        private const int WakeMilliseconds = 5;
        private readonly CallbackGate _gate = new();
        private readonly PitchPreservingSpeed _speed;
        private readonly object _stretchLock = new();
        private readonly PitchPump _pump;
        private readonly PreciseWait _wait = new();
        private readonly Thread _thread;
        private volatile bool _stop;
        private bool _started;
        private bool _disposed;
        private int _unit;

        internal PitchCorrector(AudioFrameOutputNode capture, AudioFrameInputNode render, int sampleRate, int quantumFrames, double speed)
        {
            Capture = capture;
            Render = render;
            _speed = new PitchPreservingSpeed(sampleRate, quantumFrames, speed);
            _pump = new PitchPump(quantumFrames, sampleRate, Stretch, Write);
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "Pitch corrector"
            };
        }

        internal AudioFrameOutputNode Capture { get; }
        internal AudioFrameInputNode Render { get; }

        internal void SetSpeed(double speed)
        {
            lock (_stretchLock)
            {
                if (!_disposed) _speed.SetSpeed(speed);
            }
        }

        internal void Start()
        {
            if (_started) return;
            Step();
            _started = true;
            _thread.Start();
        }

        private void Run()
        {
            try
            {
                while (!_stop)
                {
                    Step();
                    if (_stop) break;
                    _wait.Wait(WakeMilliseconds);
                }
            }
            finally
            {
                _wait.Dispose();
            }
        }

        private unsafe void Step()
        {
            if (!_gate.TryEnter()) return;
            try
            {
                var queued = _unit > 0 ? (long)Render.QueuedSampleCount / _unit : 0;
                using (var captured = Capture.GetFrame())
                using (var buffer = captured.LockBuffer(AudioBufferAccessMode.Read))
                using (var reference = buffer.CreateReference())
                {
                    reference.GetBuffer(out var bytes, out _);
                    var count = bytes == null ? 0 : (int)(buffer.Length / 4);
                    _pump.Step(new ReadOnlySpan<float>(bytes, count), queued);
                }

                if (_unit == 0)
                    _unit = (long)Render.QueuedSampleCount >= _pump.WrittenFrames * 2 ? 2 : 1;
            }
            catch
            {
            }
            finally
            {
                _gate.Exit();
            }
        }

        private void Stretch(ReadOnlySpan<float> input, Span<float> output)
        {
            lock (_stretchLock)
            {
                if (_disposed) output.Clear();
                else _speed.Process(input, output);
            }
        }

        private void Write(ReadOnlySpan<float> interleavedStereo)
        {
            using var frame = AudioFrames.FromInterleaved(interleavedStereo);
            Render.AddFrame(frame);
        }

        public void Dispose()
        {
            _stop = true;
            _gate.CloseAndDrain();
            lock (_stretchLock)
            {
                if (_disposed) return;
                _disposed = true;
                _speed.Dispose();
            }

            if (!_started) _wait.Dispose();
        }
    }
}

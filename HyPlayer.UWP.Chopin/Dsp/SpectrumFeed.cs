using System;
using System.Threading;
using HyPlayer.UWP.Chopin.Utils;
using Windows.Media;
using Windows.Media.Audio;

namespace HyPlayer.UWP.Chopin.Dsp
{
    internal sealed class SpectrumFeed
    {
        private const int WakeMilliseconds = 16;
        private const int IdleWakeMilliseconds = 100;
        private const int DrainMilliseconds = 500;
        private readonly Lock _sync = new();
        private Worker _worker;
        private int _active;
        private int _blocked;

        public void Attach(AudioFrameOutputNode node, FFTProcessor processor, int blockSamples, int sampleRate)
        {
            lock (_sync)
            {
                StopWorker();
                if (node == null || processor == null) return;
                _worker = new Worker(this, node, processor, blockSamples, sampleRate);
                _worker.Start();
            }
        }

        public void Detach()
        {
            lock (_sync) StopWorker();
        }

        public void Block()
        {
            Interlocked.Increment(ref _blocked);
            Drain();
        }

        public void Unblock() => Interlocked.Decrement(ref _blocked);

        private void StopWorker()
        {
            var worker = _worker;
            _worker = null;
            worker?.Stop();
        }

        private bool TryEnter(Worker worker)
        {
            Interlocked.Increment(ref _active);
            if (Volatile.Read(ref _blocked) == 0 && !worker.Stopping) return true;
            Interlocked.Decrement(ref _active);
            return false;
        }

        private void Exit() => Interlocked.Decrement(ref _active);

        private void Drain()
        {
            var deadline = Environment.TickCount64 + DrainMilliseconds;
            var spin = new SpinWait();
            while (Volatile.Read(ref _active) > 0 && Environment.TickCount64 < deadline) spin.SpinOnce();
        }

        private sealed class Worker
        {
            private readonly SpectrumFeed _owner;
            private readonly AudioFrameOutputNode _node;
            private readonly FFTProcessor _processor;
            private readonly int _blockSamples;
            private readonly int _sampleRate;
            private readonly PreciseWait _wait = new();
            private readonly Thread _thread;
            private volatile bool _stop;

            public Worker(SpectrumFeed owner, AudioFrameOutputNode node, FFTProcessor processor, int blockSamples, int sampleRate)
            {
                _owner = owner;
                _node = node;
                _processor = processor;
                _blockSamples = blockSamples;
                _sampleRate = sampleRate;
                _thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "Spectrum feed"
                };
            }

            public bool Stopping => _stop;

            public void Start() => _thread.Start();

            public void Stop()
            {
                _stop = true;
                _owner.Drain();
            }

            private void Run()
            {
                try
                {
                    while (!_stop)
                    {
                        var fed = Pull();
                        if (_stop) break;
                        _wait.Wait(fed ? WakeMilliseconds : IdleWakeMilliseconds);
                    }
                }
                finally
                {
                    _wait.Dispose();
                }
            }

            private unsafe bool Pull()
            {
                if (!_owner.TryEnter(this)) return false;
                try
                {
                    using var frame = _node.GetFrame();
                    using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
                    using var reference = buffer.CreateReference();
                    reference.GetBuffer(out var bytes, out _);
                    var samples = (int)(buffer.Length / sizeof(float));
                    if (bytes == null || samples <= 0) return false;
                    return _processor.ProcessBlocks(new ReadOnlySpan<float>(bytes, samples), _blockSamples, _sampleRate) > 0;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    _owner.Exit();
                }
            }
        }
    }
}

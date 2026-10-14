using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.UWP.Chopin.Utils;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Render;

namespace HyPlayer.UWP.Chopin.Dsp
{
    internal sealed class DecodedVoice : IDisposable
    {
        private const int WakeMilliseconds = 5;
        private const int GrantWaitMilliseconds = 20;
        private const double ResyncSeconds = 0.25;
        private readonly CallbackGate _gate = new();
        private readonly object _sync = new();
        private readonly AudioGraph _decoder;
        private readonly AudioFrameOutputNode _tap;
        private readonly SpeedProcessor _processor;
        private readonly AutoResetEvent _grant = new(false);
        private readonly PreciseWait _wait = new();
        private readonly Thread _thread;
        private readonly float[] _buffer;
        private readonly int _rate;
        private readonly int _lead;
        private readonly int _decoderQuantum;
        private IAudioNode _bus;
        private IAudioNode _output;
        private int _budget;
        private long _requested;
        private long _received;
        private volatile bool _released;
        private volatile bool _stop;
        private volatile bool _playing;
        private volatile bool _sourceEnded;
        private bool _endPending;
        private bool _completionRaised;
        private bool _started;
        private bool _disposed;
        private long _written;
        private int _unit;
        private TimeSpan _anchor;
        private TimeSpan _lastPosition;

        private DecodedVoice(AudioGraph decoder, MediaSourceAudioInputNode node, AudioFrameOutputNode tap, AudioFrameInputNode render,
            int sampleRate, int blockFrames, double speed, bool preservePitch)
        {
            _decoder = decoder;
            Node = node;
            _tap = tap;
            Render = render;
            _rate = sampleRate;
            _decoderQuantum = Math.Max(1, decoder.SamplesPerQuantum);
            _processor = new SpeedProcessor(sampleRate, blockFrames, speed, preservePitch);
            _buffer = new float[blockFrames * 2];
            _lead = Math.Max(blockFrames * 2, (int)(sampleRate * PitchPump.LeadSeconds));
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "Decoded voice"
            };
        }

        internal event Action<DecodedVoice> Completed;

        internal MediaSourceAudioInputNode Node { get; }
        internal AudioFrameInputNode Render { get; private set; }
        internal double Speed => _processor.Speed;
        internal bool PreservePitch => _processor.PreservePitch;

        internal TimeSpan Position
        {
            get
            {
                if (!_gate.TryEnter()) return _lastPosition;
                try
                {
                    lock (_sync)
                    {
                        if (_disposed) return _lastPosition;
                        _lastPosition = Audible();
                        return _lastPosition;
                    }
                }
                catch
                {
                    return _lastPosition;
                }
                finally
                {
                    _gate.Exit();
                }
            }
        }

        internal static async Task<DecodedVoice> CreateAsync(AudioGraph main, MediaSource source, double speed, bool preservePitch)
        {
            var rate = main.EncodingProperties.SampleRate;
            var decoder = await CreateDecoderAsync(rate);
            if (decoder == null) return null;
            MediaSourceAudioInputNode node = null;
            AudioFrameOutputNode tap = null;
            AudioFrameInputNode render = null;
            try
            {
                var created = await decoder.CreateMediaSourceAudioInputNodeAsync(source);
                if (created.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                    throw created.ExtendedError ?? new InvalidOperationException(created.Status.ToString());
                node = created.Node;
                var properties = Format(rate);
                tap = decoder.CreateFrameOutputNode(properties);
                node.AddOutgoingConnection(tap);
                render = main.CreateFrameInputNode(properties);
                render.Stop();
                var voice = new DecodedVoice(decoder, node, tap, render, (int)rate, Math.Max(1, main.SamplesPerQuantum), speed, preservePitch);
                decoder.QuantumStarted += voice.Throttle;
                node.MediaSourceCompleted += voice.OnSourceCompleted;
                return voice;
            }
            catch
            {
                render?.Dispose();
                tap?.Dispose();
                node?.Dispose();
                decoder.Dispose();
                throw;
            }
        }

        internal static AudioEncodingProperties Format(uint sampleRate)
        {
            var properties = AudioEncodingProperties.CreatePcm(sampleRate, 2, 32);
            properties.Subtype = MediaEncodingSubtypes.Float;
            return properties;
        }

        private static async Task<AudioGraph> CreateDecoderAsync(uint rate)
        {
            var settings = new AudioGraphSettings(AudioRenderCategory.Media) { EncodingProperties = Format(rate) };
            var result = await AudioGraph.CreateAsync(settings);
            if (result.Status == AudioGraphCreationStatus.Success && result.Graph.EncodingProperties.SampleRate == rate)
                return result.Graph;
            result.Graph?.Dispose();
            result = await AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.Media));
            if (result.Status == AudioGraphCreationStatus.Success && result.Graph.EncodingProperties.SampleRate == rate)
                return result.Graph;
            result.Graph?.Dispose();
            return null;
        }

        internal void Start(TimeSpan position, IAudioNode bus, IAudioNode output)
        {
            lock (_sync)
            {
                _bus = bus;
                _output = output;
                Restart(position);
            }

            Render.Start();
            if (_started) return;
            _started = true;
            _thread.Start();
        }

        internal void Play() => _playing = true;

        internal void Pause() => _playing = false;

        internal void SetSpeed(double speed, bool preservePitch)
        {
            lock (_sync)
            {
                if (!_disposed) _processor.SetSpeed(speed, preservePitch);
            }
        }

        internal void Seek(TimeSpan position)
        {
            _gate.CloseAndDrain();
            try
            {
                lock (_sync)
                {
                    if (_disposed) return;
                    Restart(position);
                    try
                    {
                        Render.DiscardQueuedFrames();
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                _gate.Reopen();
            }
        }

        internal AudioFrameInputNode Rebind(AudioFrameInputNode render, IAudioNode bus, IAudioNode output)
        {
            _gate.CloseAndDrain();
            try
            {
                lock (_sync)
                {
                    var position = _disposed ? _lastPosition : Audible();
                    var previous = Render;
                    Render = render;
                    _bus = bus;
                    _output = output;
                    _unit = 0;
                    if (!_disposed) Restart(position);
                    return previous;
                }
            }
            finally
            {
                _gate.Reopen();
            }
        }

        internal void Suspend() => _gate.CloseAndDrain();

        internal void Resume() => _gate.Reopen();

        private void Restart(TimeSpan position)
        {
            _released = true;
            _grant.Set();
            try
            {
                _decoder.Stop();
            }
            catch
            {
            }

            Node.Seek(position);
            using (_tap.GetFrame())
            {
            }

            Volatile.Write(ref _budget, 0);
            _requested = 0;
            _received = 0;
            _sourceEnded = false;
            _endPending = false;
            _completionRaised = false;
            _processor.Reset();
            _written = 0;
            _anchor = position;
            _lastPosition = position;
            _released = false;
            _decoder.Start();
        }

        private TimeSpan Audible()
        {
            var source = _processor.SourceAt(Math.Max(0, _written - Queued()));
            if (_processor.Ended) source = Math.Min(source, _processor.EndFrame);
            var position = _anchor + TimeSpan.FromSeconds(source / _rate);
            return position < TimeSpan.Zero ? TimeSpan.Zero : position;
        }

        private void Throttle(AudioGraph sender, object args)
        {
            while (!_released)
            {
                if (Interlocked.Decrement(ref _budget) >= 0) return;
                Interlocked.Increment(ref _budget);
                _grant.WaitOne(GrantWaitMilliseconds);
            }
        }

        private void OnSourceCompleted(MediaSourceAudioInputNode sender, object args) => _sourceEnded = true;

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

        private void Step()
        {
            if (!_gate.TryEnter()) return;
            Action<DecodedVoice> completed = null;
            try
            {
                lock (_sync)
                {
                    if (_disposed) return;
                    Pull();
                    if (_sourceEnded && !_processor.Ended)
                    {
                        if (_endPending)
                        {
                            var duration = Node.Duration;
                            var end = duration > _anchor ? (long)Math.Round((duration - _anchor).TotalSeconds * _rate) : _processor.BufferedEnd;
                            _processor.MarkEnd(Math.Min(end, _processor.BufferedEnd));
                        }

                        _endPending = true;
                    }

                    _processor.DownstreamGain = DownstreamGain();
                    var queued = Queued();
                    if (_playing)
                    {
                        while (queued < _lead && _processor.Render(_buffer))
                        {
                            Write(_buffer);
                            _written += _buffer.Length / 2;
                            queued += _buffer.Length / 2;
                        }

                        if (_unit == 0 && _written > 0)
                            _unit = (long)Render.QueuedSampleCount > _written * 3 / 2 ? 2 : 1;
                    }

                    Grant(queued);
                    if (!_completionRaised && _processor.Drained(Math.Max(0, _written - Queued())))
                    {
                        _completionRaised = true;
                        completed = Completed;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _gate.Exit();
            }

            if (completed != null) ThreadPool.QueueUserWorkItem(_ => completed(this));
        }

        private unsafe void Pull()
        {
            using var frame = _tap.GetFrame();
            using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
            using var reference = buffer.CreateReference();
            reference.GetBuffer(out var bytes, out _);
            var count = bytes == null ? 0 : (int)(buffer.Length / sizeof(float));
            if (count < 2) return;
            var frames = count / 2;
            if (!_processor.Ended) _processor.Append(new ReadOnlySpan<float>(bytes, frames * 2));
            _received += frames;
            var expected = _anchor + TimeSpan.FromSeconds((double)_processor.BufferedEnd / _rate);
            var drift = Node.Position - expected;
            if (Math.Abs(drift.TotalSeconds) > ResyncSeconds) _anchor += drift;
        }

        private void Grant(long queued)
        {
            if (_processor.Ended || _sourceEnded) return;
            var target = _processor.RequiredInput((int)Math.Max(0, _lead - queued) + _buffer.Length);
            var inFlight = Math.Max(0, _requested - _received);
            var deficit = target - _processor.BufferedEnd - inFlight;
            if (deficit <= 0) return;
            var quanta = (int)((deficit + _decoderQuantum - 1) / _decoderQuantum);
            _requested += (long)quanta * _decoderQuantum;
            Interlocked.Add(ref _budget, quanta);
            _grant.Set();
        }

        private long Queued() => _unit > 0 ? (long)Render.QueuedSampleCount / _unit : (long)Render.QueuedSampleCount / 2;

        private double DownstreamGain()
        {
            try
            {
                return (_bus?.OutgoingGain ?? 1) * (_output?.OutgoingGain ?? 1);
            }
            catch
            {
                return 1;
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
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _released = true;
                _grant.Set();
                try
                {
                    _decoder.Stop();
                }
                catch
                {
                }

                try
                {
                    _decoder.QuantumStarted -= Throttle;
                    Node.MediaSourceCompleted -= OnSourceCompleted;
                }
                catch
                {
                }

                _processor.Dispose();
            }

            if (!_started) _wait.Dispose();
            Node.Dispose();
            _tap.Dispose();
            _decoder.Dispose();
        }
    }
}

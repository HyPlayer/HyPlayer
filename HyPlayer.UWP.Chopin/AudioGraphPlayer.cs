using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Dsp;
using HyPlayer.UWP.Chopin.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Effects;
using Timer = System.Timers.Timer;

namespace HyPlayer.UWP.Chopin.Abstractions.Models
{
    /// <summary>
    /// 基于 AudioGraph 的音频播放器实现
    /// 提供多音轨播放、设备切换、音量控制等功能
    /// </summary>
    public partial class AudioGraphPlayer : IPlayer, IDisposable, IMixRendererHost
    {
        private const long MaxRampQuantaPerWake = 2;
        #region Private Fields
        private readonly ConcurrentDictionary<AudioGraphPlaybackSource, MediaSourceAudioInputNode> _audioInputNodes = new();
        private readonly ConcurrentDictionary<MediaSourceAudioInputNode, AudioGraphPlaybackSource> _audioInputNodesReverseDictionary = new();
        private readonly ConcurrentDictionary<AudioGraphPlaybackSource, PlaybackSourceGainState> _playbackSourceGainStates = new();
        private readonly ConcurrentDictionary<AudioGraphPlaybackSource, AudioSubmixNode> _voiceBuses = new();
        private readonly ConcurrentDictionary<AudioGraphPlaybackSource, PitchCorrector> _pitchCorrectors = new();
        private readonly ConcurrentDictionary<AudioGraphPlaybackSource, DecodedVoice> _decodedVoices = new();
        private readonly ConcurrentDictionary<AudioGraphPlaybackSource, Relocation> _relocations = new();
        private AudioGraph _defaultPlayer;
        private AudioDeviceOutputNode _outputNode;
        private AudioFrameOutputNode _frameOutputNode;
        private bool _disposedValue;
        private readonly Timer _positionTimer = new() { AutoReset = true, Interval = 100 };
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private TimeSpan _lastPosition = TimeSpan.Zero;
        private string _currentDeviceId = string.Empty;
        private double _volume = 1;
        private bool _muted;
        private double _masterFade = 1;
        private AudioGraphPlaybackSource _primaryPlaybackSource;
        private readonly object _rampLock = new();
        private readonly List<GainRamp> _ramps = new();
        private GainRamp _masterRamp;
        private int _activeRampCount;
        private int _transportVersion;
        private readonly List<PlaybackTap> _taps = new();
        private readonly Dictionary<PlaybackTap, AudioGraphPlaybackSource> _tapSources = new();
        private readonly List<MixRenderer> _renderers = new();
        private readonly object _callbackLock = new();
        private AudioGraph _callbackGraph;
        private AudioFrameOutputNode _runningFrameOutput;
        private readonly CallbackGate _callbackGate = new();
        private readonly SpectrumFeed _spectrumFeed = new();
        private bool _enableFftProcessing;
        [ThreadStatic] private static bool _inGraphCallback;
        #endregion

        private sealed class GainRamp
        {
            public AudioGraphPlaybackSource Source;
            public double From;
            public double To;
            public double Duration;
            public double Elapsed;
            public FadeCurve Curve;
            public TaskCompletionSource<bool> Completion;
        }

        private sealed class Relocation
        {
            public double Speed;
            public bool PreservePitch;
            public TimeSpan Position;
            public bool Playing;
            public bool Detached;
            public DecodedVoice Voice;
        }

        public AudioGraphPlayer()
        {
            _positionTimer.Elapsed += PositionTimer_Elapsed;
        }

        #region Public Properties
        public bool PlayerCreated => _defaultPlayer != null;
        public double Volume => _volume;
        public uint SampleRate => _defaultPlayer?.EncodingProperties.SampleRate ?? 0;
        public int SamplesPerQuantum => _defaultPlayer?.SamplesPerQuantum ?? 0;
        public bool RealtimeCallbacksActive => Volatile.Read(ref _callbackGraph) != null || MixWorkActive();

        public bool IsMuted
        {
            get => _outputNode != null && _muted;
            set
            {
                if (_outputNode == null) return;
                _muted = value;
                ApplyMasterGain();
            }
        }

        public FFTProcessor FFTProcessor = new();

        public IPlaybackSource PrimaryPlaybackSource
        {
            get => _primaryPlaybackSource;
            set
            {
                var source = value as AudioGraphPlaybackSource;
                if (ReferenceEquals(source, _primaryPlaybackSource)) return;
                _primaryPlaybackSource = source;
                OnPrimaryPlaybackSourceChanged?.Invoke(source);
            }
        }

        public PlaybackStatus GlobalPlaybackStatus { get; protected set; } = PlaybackStatus.Closed;

        public MediaSourceAudioInputNode PrimaryAudioInputNode
        {
            get
            {
                if (PrimaryPlaybackSource == null) return null;
                var source = PrimaryPlaybackSource as AudioGraphPlaybackSource;
                if (Detached(source)) return null;
                return _audioInputNodes.TryGetValue(source, out var node) ? node : null;
            }
        }

        public int ConnectedPlaybackSourceCount => _audioInputNodes.Count;
        public ISMTCManager SMTCManager { get; set; }
        public bool EnableFFTProcessing
        {
            get => _enableFftProcessing;
            set
            {
                if (_enableFftProcessing == value) return;
                _enableFftProcessing = value;
                RefreshCallbacks();
            }
        }

        #endregion

        public void SetPrimaryPlaybackSource(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            if (playbackSource is not AudioGraphPlaybackSource source
                || !_audioInputNodes.ContainsKey(source))
                throw new ArgumentException("PlaybackSource is not connected.", nameof(playbackSource));

            PrimaryPlaybackSource = source;
        }

        #region Events
        public delegate void PositionChangeHandler(TimeSpan position);
        public event PositionChangeHandler OnPositionChanged;
        public event PositionChangeHandler OnIdlePositionChanged;

        public delegate void TrackReachesEndHandler(IPlaybackSource source);
        public event TrackReachesEndHandler OnTrackReachesEnd;

        public delegate void PlaybackSourceStatusChangeHandler(IPlaybackSource source, PlaybackStatus status);
        public event PlaybackSourceStatusChangeHandler OnPlaybackSourceStatusChanged;

        public delegate void GlobalPlaybackStatusChangeHandler(PlaybackStatus status);
        public event GlobalPlaybackStatusChangeHandler OnGlobalPlaybackStatusChanged;

        public delegate void PrimaryPlaybackSourceChangeHandler(IPlaybackSource source);
        public event PrimaryPlaybackSourceChangeHandler OnPrimaryPlaybackSourceChanged;

        public event Action OnGraphRebuilt;
        #endregion

        #region Initialization and Cleanup
        public async Task InitializePlayer(IAudioSettings settings)
        {
            if (settings is not AudioGraphAudioSetting audioGraphSetting)
                throw new ArgumentException("Setting is not AudioGraphSetting");

            await _lifecycleGate.WaitAsync();
            try
            {
                if (PlayerCreated)
                    return;

                AudioGraph newGraph = null;
                AudioDeviceOutputNode newOutputNode = null;
                AudioFrameOutputNode newFrameOutputNode = null;
                try
                {
                    (newGraph, newOutputNode) = await CreateGraphAsync(audioGraphSetting);
                    var encodingProperties = newOutputNode.EncodingProperties.Copy();
                    encodingProperties.ChannelCount = 1;
                    newFrameOutputNode = newGraph.CreateFrameOutputNode(encodingProperties);
                    newFrameOutputNode.Stop();
                    _volume = audioGraphSetting.OutputVolume;
                    _outputNode = newOutputNode;
                    _frameOutputNode = newFrameOutputNode;
                    ApplyMasterGain();
                    _currentDeviceId = audioGraphSetting.ResolvedDeviceId;
                    _enableFftProcessing = settings.EnableFFTProcessing;
                    newGraph.UnrecoverableErrorOccurred += GraphOnUnrecoverableErrorOccurred;
                    Volatile.Write(ref _defaultPlayer, newGraph);
                    RefreshCallbacks();
                    _positionTimer.Start();

                    newGraph = null;
                    newOutputNode = null;
                    newFrameOutputNode = null;
                }
                catch
                {
                    newFrameOutputNode?.Dispose();
                    newOutputNode?.Dispose();
                    newGraph?.Dispose();
                    throw;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public event Action<AudioGraphUnrecoverableError> OnDeviceLost;

        private void GraphOnUnrecoverableErrorOccurred(AudioGraph sender, AudioGraphUnrecoverableErrorOccurredEventArgs args)
        {
            if (!ReferenceEquals(sender, _defaultPlayer)) return;
            _currentDeviceId = string.Empty;
            OnDeviceLost?.Invoke(args.Error);
        }

        private static async Task<(AudioGraph graph, AudioDeviceOutputNode output)> CreateGraphAsync(AudioGraphAudioSetting audioGraphSetting)
        {
            var setting = await audioGraphSetting.GetAudioGraphSettingsAsync();
            var graphResult = await AudioGraph.CreateAsync(setting);
            if (graphResult.Status == AudioGraphCreationStatus.Success)
            {
                var outputResult = await graphResult.Graph.CreateDeviceOutputNodeAsync();
                if (outputResult.Status == AudioDeviceNodeCreationStatus.Success)
                    return (graphResult.Graph, outputResult.DeviceOutputNode);
                graphResult.Graph.Dispose();
                if (audioGraphSetting.PreferredSampleRate == 0) throw outputResult.ExtendedError;
            }
            else if (audioGraphSetting.PreferredSampleRate == 0)
            {
                throw graphResult.ExtendedError;
            }

            audioGraphSetting.DisablePreferredSampleRate();
            setting = await audioGraphSetting.GetAudioGraphSettingsAsync();
            graphResult = await AudioGraph.CreateAsync(setting);
            if (graphResult.Status != AudioGraphCreationStatus.Success)
                throw graphResult.ExtendedError;
            var fallbackOutput = await graphResult.Graph.CreateDeviceOutputNodeAsync();
            if (fallbackOutput.Status != AudioDeviceNodeCreationStatus.Success)
            {
                graphResult.Graph.Dispose();
                throw fallbackOutput.ExtendedError;
            }
            return (graphResult.Graph, fallbackOutput.DeviceOutputNode);
        }

        private void GraphOnQuantumStarted(AudioGraph sender, object args)
        {
            if (!_callbackGate.TryEnter()) return;
            AudioThreadCallbacks.Mark();
            _inGraphCallback = true;
            try
            {
                AdvanceRamps(sender, 1);
            }
            finally
            {
                _inGraphCallback = false;
                _callbackGate.Exit();
            }
        }

        void IMixRendererHost.MixQuantaElapsed(MixRenderer renderer, long quanta)
        {
            if (Volatile.Read(ref _activeRampCount) == 0 || !ReferenceEquals(RampDriver(), renderer)) return;
            var graph = _defaultPlayer;
            if (graph != null) AdvanceRamps(graph, Math.Min(quanta, MaxRampQuantaPerWake));
        }

        void IMixRendererHost.MixRendererStopped()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (!_disposedValue) RefreshCallbacks();
                }
                catch
                {
                }
            });
        }

        private MixRenderer RampDriver()
        {
            lock (_renderers)
            {
                foreach (var renderer in _renderers)
                    if (renderer.DrivesRamps) return renderer;
            }

            return null;
        }

        private void AdvanceRamps(AudioGraph sender, long quanta)
        {
            if (Volatile.Read(ref _activeRampCount) == 0) return;
            var sampleRate = sender.EncodingProperties.SampleRate;
            if (sampleRate == 0) return;
            var step = quanta * sender.SamplesPerQuantum / (double)sampleRate;
            List<GainRamp> finished = null;
            lock (_rampLock)
            {
                if (_masterRamp != null)
                {
                    _masterFade = Advance(_masterRamp, step);
                    ApplyMasterGain();
                    if (_masterRamp.Elapsed >= _masterRamp.Duration)
                    {
                        (finished ??= new()).Add(_masterRamp);
                        _masterRamp = null;
                    }
                }

                for (var i = _ramps.Count - 1; i >= 0; i--)
                {
                    var ramp = _ramps[i];
                    ApplyRampVolume(ramp.Source, Advance(ramp, step));
                    if (ramp.Elapsed < ramp.Duration) continue;
                    _ramps.RemoveAt(i);
                    (finished ??= new()).Add(ramp);
                }

                UpdateActiveRampCount();
            }

            if (finished == null) return;
            foreach (var ramp in finished) ramp.Completion.TrySetResult(true);
            ThreadPool.QueueUserWorkItem(_ => RefreshCallbacks());
        }

        private bool TransientCallbacksNeeded() => Volatile.Read(ref _activeRampCount) > 0 && RampDriver() == null;

        private bool MixWorkActive()
        {
            lock (_taps)
                if (_taps.Count > 0) return true;
            lock (_renderers) return _renderers.Count > 0;
        }

        private void RefreshCallbacks()
        {
            if (_inGraphCallback)
            {
                ThreadPool.QueueUserWorkItem(_ => RefreshCallbacks());
                return;
            }

            lock (_callbackLock)
            {
                var graph = _defaultPlayer;
                RealtimeGcGuard.Set(graph != null && TransientCallbacksNeeded());
                var target = graph != null && TransientCallbacksNeeded() ? graph : null;
                if (!ReferenceEquals(target, _callbackGraph))
                {
                    if (_callbackGraph != null)
                    {
                        _callbackGraph.QuantumStarted -= GraphOnQuantumStarted;
                        _callbackGraph = null;
                    }

                    _callbackGate.CloseAndDrain();
                    if (target != null)
                    {
                        _callbackGate.Reopen();
                        target.QuantumStarted += GraphOnQuantumStarted;
                        _callbackGraph = target;
                    }
                }

                var frameOutput = graph != null && _enableFftProcessing ? _frameOutputNode : null;
                if (ReferenceEquals(frameOutput, _runningFrameOutput)) return;
                _spectrumFeed.Detach();
                try
                {
                    _runningFrameOutput?.Stop();
                }
                catch
                {
                }

                _runningFrameOutput = frameOutput;
                if (frameOutput == null) return;
                try
                {
                    frameOutput.Start();
                    _spectrumFeed.Attach(frameOutput, FFTProcessor, graph.SamplesPerQuantum,
                        (int)graph.EncodingProperties.SampleRate);
                }
                catch
                {
                }
            }
        }

        public PlaybackTap AttachTap(IPlaybackSource playbackSource, double seconds = 4)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            var properties = Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm(_defaultPlayer.EncodingProperties.SampleRate, 2, 32);
            properties.Subtype = Windows.Media.MediaProperties.MediaEncodingSubtypes.Float;
            var output = _defaultPlayer.CreateFrameOutputNode(properties);
            if (_decodedVoices.TryGetValue((AudioGraphPlaybackSource)playbackSource, out var voice))
            {
                voice.Suspend();
                try
                {
                    voice.Render.AddOutgoingConnection(output);
                }
                finally
                {
                    voice.Resume();
                }
            }
            else
            {
                node.AddOutgoingConnection(output);
            }
            var tap = new PlaybackTap(output, (int)properties.SampleRate, seconds);
            lock (_taps)
            {
                _taps.Add(tap);
                _tapSources[tap] = (AudioGraphPlaybackSource)playbackSource;
            }

            RefreshCallbacks();
            return tap;
        }

        public void DetachTap(IPlaybackSource playbackSource, PlaybackTap tap)
        {
            if (tap == null) return;
            AudioGraphPlaybackSource owner;
            lock (_taps)
            {
                if (!_taps.Remove(tap)) return;
                _tapSources.Remove(tap, out owner);
            }

            tap.Gate.CloseAndDrain();
            try
            {
                var source = playbackSource as AudioGraphPlaybackSource ?? owner;
                if (source != null && _decodedVoices.TryGetValue(source, out var voice))
                {
                    voice.Suspend();
                    try
                    {
                        voice.Render.RemoveOutgoingConnection(tap.Node);
                    }
                    finally
                    {
                        voice.Resume();
                    }
                }
                else if (source != null && _audioInputNodes.TryGetValue(source, out var node))
                {
                    node.RemoveOutgoingConnection(tap.Node);
                }
            }
            catch
            {
            }

            DisposeInBackground(tap.Node);
            RefreshCallbacks();
        }

        private void DetachTaps(AudioGraphPlaybackSource source)
        {
            PlaybackTap[] orphanTaps;
            lock (_taps)
                orphanTaps = _tapSources.Where(pair => ReferenceEquals(pair.Value, source)).Select(pair => pair.Key).ToArray();
            foreach (var tap in orphanTaps) DetachTap(source, tap);
        }

        public MixRenderer CreateMixRenderer(IMixPumpSource source)
        {
            ThrowExceptionIfDisposed();
            var graph = _defaultPlayer;
            var properties = Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm(graph.EncodingProperties.SampleRate, 2, 32);
            properties.Subtype = Windows.Media.MediaProperties.MediaEncodingSubtypes.Float;
            var node = graph.CreateFrameInputNode(properties);
            node.Stop();
            var bus = graph.CreateSubmixNode();
            bus.AddOutgoingConnection(_outputNode);
            ConnectFrameOutput(bus);
            node.AddOutgoingConnection(bus);
            var renderer = new MixRenderer(graph, node, bus, source, this);
            lock (_renderers) _renderers.Add(renderer);
            RefreshCallbacks();
            renderer.Start();
            return renderer;
        }

        public void ReleaseMixRenderer(MixRenderer renderer)
        {
            if (renderer == null) return;
            lock (_renderers) _renderers.Remove(renderer);
            RefreshCallbacks();
            renderer.Shutdown(false);
            RefreshCallbacks();
            try
            {
                renderer.Node.Stop();
                renderer.Node.RemoveOutgoingConnection(renderer.Bus);
                renderer.Bus.RemoveOutgoingConnection(_outputNode);
                DisconnectFrameOutput(renderer.Bus);
            }
            catch
            {
            }

            DisposeInBackground(renderer.Node);
            DisposeInBackground(renderer.Bus);
        }

        private static double Advance(GainRamp ramp, double step)
        {
            ramp.Elapsed = Math.Min(ramp.Duration, ramp.Elapsed + step);
            var t = ramp.Duration <= 0 ? 1 : ramp.Elapsed / ramp.Duration;
            var shaped = ramp.Curve == FadeCurve.EqualPower
                ? (ramp.To >= ramp.From ? Math.Sin(t * Math.PI / 2) : 1 - Math.Cos(t * Math.PI / 2))
                : t;
            return ramp.From + (ramp.To - ramp.From) * shaped;
        }

        private void ApplyRampVolume(AudioGraphPlaybackSource source, double volume)
        {
            if (!_voiceBuses.TryGetValue(source, out var bus)
                || !_playbackSourceGainStates.TryGetValue(source, out var state))
                return;

            try
            {
                state.Apply(bus, volume, false);
            }
            catch
            {
            }
        }

        private void UpdateActiveRampCount()
        {
            Volatile.Write(ref _activeRampCount, _ramps.Count + (_masterRamp != null ? 1 : 0));
        }

        private void ApplyMasterGain()
        {
            if (_outputNode == null) return;
            _outputNode.OutgoingGain = _muted ? 0 : _volume * _masterFade;
        }

        public async Task ChangePlayerServiceImplementation(IAudioSettings settings)
        {
            ThrowExceptionIfDisposed();
            if (settings is not AudioGraphAudioSetting audioGraphSetting)
                throw new ArgumentException("Setting is not AudioGraphSetting");

            await _lifecycleGate.WaitAsync();
            try
            {
                if (_defaultPlayer == null || _currentDeviceId == audioGraphSetting.ResolvedDeviceId)
                    return;

                _positionTimer.Stop();
                var oldPlayer = _defaultPlayer;
                var oldOutputNode = _outputNode;
                var oldNodes = _audioInputNodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                AudioGraph newPlayer = null;
                AudioDeviceOutputNode newOutputNode = null;
                AudioFrameOutputNode newFrameOutputNode = null;
                var newNodes = new ConcurrentDictionary<AudioGraphPlaybackSource, MediaSourceAudioInputNode>();
                var newBuses = new ConcurrentDictionary<AudioGraphPlaybackSource, AudioSubmixNode>();
                var committed = false;
                var pendingRelocations = new List<(AudioGraphPlaybackSource Source, Relocation Request)>();

                try
                {
                    // 创建新的 AudioGraph
                    (newPlayer, newOutputNode) = await CreateGraphAsync(audioGraphSetting);

                    try
                    {
                        oldPlayer.Stop();
                    }
                    catch
                    {
                    }
                    DetachCallbacks();
                    oldPlayer.UnrecoverableErrorOccurred -= GraphOnUnrecoverableErrorOccurred;
                    newOutputNode.OutgoingGain = oldOutputNode.OutgoingGain;

                    var encodingProperties = newOutputNode.EncodingProperties.Copy();
                    encodingProperties.ChannelCount = 1;
                    newFrameOutputNode = newPlayer.CreateFrameOutputNode(encodingProperties);
                    newFrameOutputNode.Stop();

                    // 转移所有播放源
                    var newNodesReverse = new ConcurrentDictionary<MediaSourceAudioInputNode, AudioGraphPlaybackSource>();
                    var keptVoices = new List<(DecodedVoice Voice, AudioSubmixNode Bus)>();
                    var sameRate = newPlayer.EncodingProperties.SampleRate == oldPlayer.EncodingProperties.SampleRate;

                    foreach (var (source, oldNode) in oldNodes)
                    {
                        if (_decodedVoices.TryGetValue(source, out var decoded))
                        {
                            if (sameRate)
                            {
                                var keptBus = newPlayer.CreateSubmixNode();
                                newBuses[source] = keptBus;
                                keptBus.OutgoingGain = _playbackSourceGainStates.TryGetValue(source, out var keptGain) ? keptGain.GetOutgoingGain() : 1;
                                keptBus.AddOutgoingConnection(newOutputNode);
                                keptBus.AddOutgoingConnection(newFrameOutputNode);
                                newNodes[source] = oldNode;
                                newNodesReverse[oldNode] = source;
                                keptVoices.Add((decoded, keptBus));
                                continue;
                            }

                            var request = new Relocation { Speed = decoded.Speed, PreservePitch = decoded.PreservePitch };
                            if (_relocations.TryAdd(source, request)) pendingRelocations.Add((source, request));
                        }

                        // 捕获旧节点状态
                        var position = PositionOf(source, oldNode);
                        var gain = _playbackSourceGainStates.TryGetValue(source, out var gainState)
                            ? gainState.GetOutgoingGain()
                            : oldNode.OutgoingGain;
                        var factor = SpeedOf(source, oldNode);
                        var effects = oldNode.EffectDefinitions.ToList();

                        // 清理旧节点
                        UnsubscribeCompletion(oldNode);
                        if (_decodedVoices.TryRemove(source, out var replaced))
                        {
                            replaced.Completed -= OnVoiceCompleted;
                            replaced.Dispose();
                            DisposeInBackground(replaced.Render);
                        }
                        else
                        {
                            oldNode.Dispose();
                        }
                        source.PlaybackSource?.Reset();

                        // 准备播放源
                        if (source.PlaybackSource == null)
                            await source.CreatePlaybackSource();
                        await source.PlaybackSource?.OpenAsync();

                        // 在新图中创建节点
                        var createResult = await newPlayer.CreateMediaSourceAudioInputNodeAsync(source.PlaybackSource);
                        if (createResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                            throw createResult.ExtendedError;
                        var newNode = createResult.Node;
                        newNodes[source] = newNode;
                        newNodesReverse[newNode] = source;

                        // 应用状态
                        newNode.PlaybackSpeedFactor = factor;
                        var bus = newPlayer.CreateSubmixNode();
                        newBuses[source] = bus;
                        bus.OutgoingGain = gain;
                        bus.AddOutgoingConnection(newOutputNode);
                        bus.AddOutgoingConnection(newFrameOutputNode);
                        newNode.AddOutgoingConnection(bus);
                        newNode.MediaSourceCompleted += OnMediaSourceCompleted;

                        // 应用效果
                        foreach (var effect in effects)
                        {
                            newNode.EffectDefinitions.Add(effect);
                        }

                        // 恢复位置并开始
                        await Task.Delay(250);
                        newNode.Seek(position);
                        newNode.Start();

                    }

                    // 替换为新图
                    _defaultPlayer = newPlayer;
                    newPlayer.UnrecoverableErrorOccurred += GraphOnUnrecoverableErrorOccurred;
                    _enableFftProcessing = settings.EnableFFTProcessing;
                    _outputNode = newOutputNode;
                    _frameOutputNode = newFrameOutputNode;
                    _audioInputNodes.Clear();
                    foreach (var kvp in newNodes) _audioInputNodes.TryAdd(kvp.Key, kvp.Value);
                    _audioInputNodesReverseDictionary.Clear();
                    foreach (var kvp in newNodesReverse) _audioInputNodesReverseDictionary.TryAdd(kvp.Key, kvp.Value);
                    var pitchSources = _pitchCorrectors.Keys.ToList();
                    foreach (var stale in _pitchCorrectors.Values) stale.Dispose();
                    _pitchCorrectors.Clear();
                    _voiceBuses.Clear();
                    foreach (var kvp in newBuses) _voiceBuses.TryAdd(kvp.Key, kvp.Value);
                    foreach (var (voice, voiceBus) in keptVoices)
                    {
                        try
                        {
                            var render = newPlayer.CreateFrameInputNode(DecodedVoice.Format(newPlayer.EncodingProperties.SampleRate));
                            render.Stop();
                            render.AddOutgoingConnection(voiceBus);
                            DisposeInBackground(voice.Rebind(render, voiceBus, newOutputNode));
                            render.Start();
                        }
                        catch
                        {
                        }
                    }
                    foreach (var pitchSource in pitchSources)
                    {
                        if (_audioInputNodes.TryGetValue(pitchSource, out var pitchNode) && Math.Abs(pitchNode.PlaybackSpeedFactor - 1) > 0.001)
                            AttachPitchCorrector(pitchSource, pitchNode, pitchNode.PlaybackSpeedFactor);
                    }
                    lock (_taps)
                    {
                        _taps.Clear();
                        _tapSources.Clear();
                    }

                    ShutdownMixRenderers(true);
                    RefreshCallbacks();

                    _currentDeviceId = audioGraphSetting.ResolvedDeviceId;
                    if (GlobalPlaybackStatus == PlaybackStatus.Playing) newPlayer.Start();
                    committed = true;
                    foreach (var (source, request) in pendingRelocations) _ = RelocateAsync(source, request);

                    OnGraphRebuilt?.Invoke();
                }
                finally
                {
                    if (committed)
                    {
                        oldPlayer.Dispose();
                    }
                    else
                    {
                        foreach (var (source, _) in pendingRelocations) _relocations.TryRemove(source, out _);
                        foreach (var (source, node) in newNodes)
                        {
                            try { node.MediaSourceCompleted -= OnMediaSourceCompleted; } catch { }
                            if (newBuses.TryGetValue(source, out var bus))
                                try { node.RemoveOutgoingConnection(bus); } catch { }
                            try { node.Dispose(); } catch { }
                        }

                        foreach (var bus in newBuses.Values)
                        {
                            try { bus.Dispose(); } catch { }
                        }

                        RefreshCallbacks();

                        newOutputNode?.Dispose();
                        newFrameOutputNode?.Dispose();
                        newPlayer?.Dispose();
                    }
                    _positionTimer.Start();
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue) return;

            if (disposing)
            {
                _positionTimer?.Stop();
                _positionTimer.Elapsed -= PositionTimer_Elapsed;
                _positionTimer?.Dispose();
                _lifecycleGate.Dispose();
            }

            var player = _defaultPlayer;
            CancelAllRamps();
            ShutdownMixRenderers(false);
            DetachCallbacks();
            RealtimeGcGuard.Set(false);
            foreach (var corrector in _pitchCorrectors.Values) corrector.Dispose();
            _pitchCorrectors.Clear();
            foreach (var source in _decodedVoices.Keys.ToList()) DetachDecodedVoice(source);
            if (player is not null)
                player.UnrecoverableErrorOccurred -= GraphOnUnrecoverableErrorOccurred;

            // 清理所有播放源
            foreach (var (source, item) in _audioInputNodes)
            {
                UnsubscribeCompletion(item);
                if (_voiceBuses.TryRemove(source, out var bus))
                {
                    try
                    {
                        item.RemoveOutgoingConnection(bus);
                    }
                    catch
                    {
                    }

                    bus.Dispose();
                }

                try
                {
                    item.Dispose();
                }
                catch
                {
                }
            }

            _audioInputNodes.Clear();
            _audioInputNodesReverseDictionary.Clear();
            _playbackSourceGainStates.Clear();
            _primaryPlaybackSource = null;
            player?.Dispose();
            _disposedValue = true;
        }

        private void ShutdownMixRenderers(bool failed)
        {
            MixRenderer[] stale;
            lock (_renderers)
            {
                stale = _renderers.ToArray();
                _renderers.Clear();
            }

            foreach (var renderer in stale) renderer.Shutdown(failed);
        }

        private void DetachCallbacks()
        {
            lock (_callbackLock)
            {
                if (_callbackGraph != null)
                {
                    _callbackGraph.QuantumStarted -= GraphOnQuantumStarted;
                    _callbackGraph = null;
                }

                _spectrumFeed.Detach();
                _runningFrameOutput = null;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~AudioGraphPlayer()
        {
            Dispose(disposing: false);
        }
        #endregion

        #region Playback Control Methods
        public void PlayAll() => PlayAll(TimeSpan.Zero);

        public void PlayAll(TimeSpan fadeIn)
        {
            ThrowExceptionIfDisposed();
            if (_defaultPlayer == null || ConnectedPlaybackSourceCount == 0) return;

            var wasPlaying = GlobalPlaybackStatus == PlaybackStatus.Playing;
            Interlocked.Increment(ref _transportVersion);
            if (fadeIn > TimeSpan.Zero)
                _ = RampMasterAsync(wasPlaying ? _masterFade : 0, 1, fadeIn);
            else if (!wasPlaying || IsMasterFadingOut())
                CancelMasterRamp(1);

            if (!wasPlaying)
                _defaultPlayer.Start();
            UpdateGlobalPlaybackStatus(PlaybackStatus.Playing);
        }

        public void PauseAll()
        {
            ThrowExceptionIfDisposed();
            if (_defaultPlayer == null) return;

            Interlocked.Increment(ref _transportVersion);
            if (GlobalPlaybackStatus != PlaybackStatus.Paused)
                _defaultPlayer.Stop();
            CancelMasterRamp(1);
            UpdateGlobalPlaybackStatus(PlaybackStatus.Paused);
        }

        public async Task PauseAllAsync(TimeSpan fadeOut)
        {
            ThrowExceptionIfDisposed();
            if (_defaultPlayer == null || GlobalPlaybackStatus != PlaybackStatus.Playing) return;
            if (fadeOut <= TimeSpan.Zero)
            {
                PauseAll();
                return;
            }

            var version = Interlocked.Increment(ref _transportVersion);
            var completed = await RampMasterAsync(_masterFade, 0, fadeOut);
            if (!completed || version != Volatile.Read(ref _transportVersion)) return;
            PauseAll();
        }

        public void PlayPlaybackSource(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);

            Route((AudioGraphPlaybackSource)playbackSource, node, n => n.Start(), v => v.Play(), r => r.Playing = true);
            if (playbackSource is AudioGraphPlaybackSource source)
            {
                source.PlaybackStatus = PlaybackStatus.Playing;
                OnPlaybackSourceStatusChanged?.Invoke(playbackSource, PlaybackStatus.Playing);
            }
        }

        public void PausePlaybackSource(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);

            Route((AudioGraphPlaybackSource)playbackSource, node, n => n.Stop(), v => v.Pause(), r => r.Playing = false);
            if (playbackSource is AudioGraphPlaybackSource source)
            {
                source.PlaybackStatus = PlaybackStatus.Paused;
                OnPlaybackSourceStatusChanged?.Invoke(playbackSource, PlaybackStatus.Paused);
            }
        }

        public void SeekPlaybackSource(TimeSpan target, IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            var source = playbackSource as AudioGraphPlaybackSource;
            // 确保不超过音频源时长
            if (source?.PlaybackSource?.Duration != null)
            {
                var value = Math.Min(target.TotalMilliseconds, source.PlaybackSource.Duration.Value.TotalMilliseconds);
                target = TimeSpan.FromMilliseconds(value);
            }

            Route(source, node, n => n.Seek(target), v => v.Seek(target), r => r.Position = target);
            if (GlobalPlaybackStatus is PlaybackStatus.Playing || !ReferenceEquals(playbackSource, PrimaryPlaybackSource))
                return;

            _lastPosition = target;
            OnIdlePositionChanged?.Invoke(target);
            PublishTimeline(target, SafeDuration(node));
        }

        private void PublishTimeline(TimeSpan position, TimeSpan duration)
        {
            SMTCManager?.OnPositionChange(new SystemMediaTransportControlsTimelineProperties
            {
                Position = position,
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                EndTime = duration,
                MaxSeekTime = duration
            });
        }
        #endregion

        #region Volume and Speed Control
        public void SetOutputVolume(double volume)
        {
            ThrowExceptionIfDisposed();
            if (_outputNode != null)
            {
                _volume = volume;
                ApplyMasterGain();
            }
        }

        public void SetPlaybackSourceOutputVolume(double volume, IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            if (playbackSource is AudioGraphPlaybackSource source)
                CancelRamp(source);
            SetPlaybackSourceGainComponent(playbackSource, volume, false);
        }

        public double GetPlaybackSourceOutputVolume(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            GetAudioInputNodeOrThrow(playbackSource);
            return _playbackSourceGainStates.TryGetValue((AudioGraphPlaybackSource)playbackSource, out var state)
                ? state.GetVolume()
                : throw new ArgumentException("PlaybackSource gain state is unavailable.", nameof(playbackSource));
        }

        public Task<bool> RampPlaybackSourceVolumeAsync(IPlaybackSource playbackSource, double target, TimeSpan duration, FadeCurve curve = FadeCurve.EqualPower)
        {
            ThrowExceptionIfDisposed();
            if (double.IsNaN(target) || double.IsInfinity(target) || target < 0)
                throw new ArgumentOutOfRangeException(nameof(target));

            var bus = GetVoiceBusOrThrow(playbackSource);
            var source = (AudioGraphPlaybackSource)playbackSource;
            if (!_playbackSourceGainStates.TryGetValue(source, out var state))
                throw new ArgumentException("PlaybackSource gain state is unavailable.", nameof(playbackSource));

            var ramp = new GainRamp
            {
                Source = source,
                From = state.GetVolume(),
                To = target,
                Duration = Math.Max(0, duration.TotalSeconds),
                Curve = curve,
                Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            lock (_rampLock)
            {
                for (var i = _ramps.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(_ramps[i].Source, source)) continue;
                    _ramps[i].Completion.TrySetResult(false);
                    _ramps.RemoveAt(i);
                }

                if (ramp.Duration <= 0)
                {
                    state.Apply(bus, target, false);
                    ramp.Completion.TrySetResult(true);
                }
                else
                {
                    _ramps.Add(ramp);
                }

                UpdateActiveRampCount();
            }

            RefreshCallbacks();
            return ramp.Completion.Task;
        }

        private Task<bool> RampMasterAsync(double from, double to, TimeSpan duration)
        {
            var ramp = new GainRamp
            {
                From = from,
                To = to,
                Duration = Math.Max(0, duration.TotalSeconds),
                Curve = FadeCurve.EqualPower,
                Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            lock (_rampLock)
            {
                _masterRamp?.Completion.TrySetResult(false);
                _masterFade = from;
                ApplyMasterGain();
                _masterRamp = ramp;
                UpdateActiveRampCount();
            }

            RefreshCallbacks();
            return ramp.Completion.Task;
        }

        private bool IsMasterFadingOut()
        {
            lock (_rampLock)
            {
                return _masterRamp != null && _masterRamp.To < _masterRamp.From;
            }
        }

        private void CancelMasterRamp(double value)
        {
            lock (_rampLock)
            {
                _masterRamp?.Completion.TrySetResult(false);
                _masterRamp = null;
                _masterFade = value;
                ApplyMasterGain();
                UpdateActiveRampCount();
            }

            RefreshCallbacks();
        }

        private void CancelRamp(AudioGraphPlaybackSource source)
        {
            lock (_rampLock)
            {
                for (var i = _ramps.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(_ramps[i].Source, source)) continue;
                    _ramps[i].Completion.TrySetResult(false);
                    _ramps.RemoveAt(i);
                }

                UpdateActiveRampCount();
            }

            RefreshCallbacks();
        }

        private void CancelAllRamps()
        {
            lock (_rampLock)
            {
                foreach (var ramp in _ramps) ramp.Completion.TrySetResult(false);
                _ramps.Clear();
                _masterRamp?.Completion.TrySetResult(false);
                _masterRamp = null;
                _masterFade = 1;
                UpdateActiveRampCount();
            }

            RefreshCallbacks();
        }

        public void SetPlaybackSourceAudioGain(double audioGain, IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            SetPlaybackSourceGainComponent(playbackSource, audioGain, true);
        }

        public double GetPlaybackSourceAudioGain(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            GetAudioInputNodeOrThrow(playbackSource);
            return _playbackSourceGainStates.TryGetValue((AudioGraphPlaybackSource)playbackSource, out var state)
                ? state.GetAudioGain()
                : throw new ArgumentException("PlaybackSource gain state is unavailable.", nameof(playbackSource));
        }

        public void SetPlaybackSourceSpeed(double speed, IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            Route((AudioGraphPlaybackSource)playbackSource, node, n => n.PlaybackSpeedFactor = speed,
                v => v.SetSpeed(speed, v.PreservePitch), r => r.Speed = speed);
        }

        public void SetUserPlaybackSpeed(double speed, IPlaybackSource playbackSource, bool preservePitch)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            var source = (AudioGraphPlaybackSource)playbackSource;
            if (_relocations.TryGetValue(source, out var relocation))
            {
                lock (relocation)
                {
                    relocation.Speed = speed;
                    relocation.PreservePitch = preservePitch;
                    relocation.Voice?.SetSpeed(speed, preservePitch);
                }

                return;
            }

            if (_decodedVoices.TryGetValue(source, out var voice))
            {
                voice.SetSpeed(speed, preservePitch);
                return;
            }

            if (preservePitch && Math.Abs(speed - 1) > 0.001 && TimeStretcher.IsAvailable && _defaultPlayer != null)
            {
                var request = new Relocation { Speed = speed, PreservePitch = true };
                if (_relocations.TryAdd(source, request)) _ = RelocateAsync(source, request);
                return;
            }

            node.PlaybackSpeedFactor = speed;
            if (preservePitch && Math.Abs(speed - 1) > 0.001 && TimeStretcher.CanTranspose)
            {
                if (_pitchCorrectors.TryGetValue(source, out var corrector))
                    corrector.SetSpeed(speed);
                else
                    AttachPitchCorrector(source, node, speed);
            }
            else
            {
                DetachPitchCorrector(source, node);
            }
        }

        private PitchCorrector AttachPitchCorrector(AudioGraphPlaybackSource source, MediaSourceAudioInputNode node, double speed)
        {
            if (_defaultPlayer == null || !_voiceBuses.TryGetValue(source, out var bus)) return null;
            PitchCorrector corrector = null;
            try
            {
                var properties = Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm(_defaultPlayer.EncodingProperties.SampleRate, 2, 32);
                properties.Subtype = Windows.Media.MediaProperties.MediaEncodingSubtypes.Float;
                var capture = _defaultPlayer.CreateFrameOutputNode(properties);
                var render = _defaultPlayer.CreateFrameInputNode(properties);
                render.Stop();
                corrector = new PitchCorrector(capture, render, (int)properties.SampleRate, _defaultPlayer.SamplesPerQuantum, speed);
                render.AddOutgoingConnection(bus);
                node.AddOutgoingConnection(capture);
                node.RemoveOutgoingConnection(bus);
                corrector.Start();
                render.Start();
                _pitchCorrectors[source] = corrector;
                return corrector;
            }
            catch
            {
                if (corrector != null)
                {
                    corrector.Dispose();
                    try
                    {
                        node.AddOutgoingConnection(bus);
                        node.RemoveOutgoingConnection(corrector.Capture);
                        corrector.Render.Stop();
                        corrector.Render.RemoveOutgoingConnection(bus);
                    }
                    catch
                    {
                    }

                    DisposeInBackground(corrector.Capture);
                    DisposeInBackground(corrector.Render);
                }

                return null;
            }
        }

        private void DetachPitchCorrector(AudioGraphPlaybackSource source, MediaSourceAudioInputNode node)
        {
            if (!_pitchCorrectors.TryRemove(source, out var corrector)) return;
            corrector.Dispose();
            try
            {
                if (_voiceBuses.TryGetValue(source, out var bus)) node.AddOutgoingConnection(bus);
                node.RemoveOutgoingConnection(corrector.Capture);
                corrector.Render.Stop();
                if (bus != null) corrector.Render.RemoveOutgoingConnection(bus);
            }
            catch
            {
            }

            DisposeInBackground(corrector.Capture);
            DisposeInBackground(corrector.Render);
        }

        public double GetPlaybackSourceSpeed(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            return SpeedOf((AudioGraphPlaybackSource)playbackSource, node);
        }

        public TimeSpan GetPlaybackSourcePosition(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            return PositionOf((AudioGraphPlaybackSource)playbackSource, node);
        }

        public TimeSpan GetPosition(MediaSourceAudioInputNode node)
        {
            if (node == null) return TimeSpan.Zero;
            if (_audioInputNodesReverseDictionary.TryGetValue(node, out var source)) return PositionOf(source, node);
            try
            {
                return node.Position;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        public double GetSpeed(MediaSourceAudioInputNode node)
        {
            if (node == null) return 1;
            if (_audioInputNodesReverseDictionary.TryGetValue(node, out var source)) return SpeedOf(source, node);
            try
            {
                return node.PlaybackSpeedFactor;
            }
            catch
            {
                return 1;
            }
        }

        private TimeSpan PositionOf(AudioGraphPlaybackSource source, MediaSourceAudioInputNode node)
        {
            if (_relocations.TryGetValue(source, out var relocation))
            {
                lock (relocation)
                {
                    if (relocation.Voice != null) return relocation.Voice.Position;
                    if (relocation.Detached) return relocation.Position;
                }
            }

            if (_decodedVoices.TryGetValue(source, out var voice)) return voice.Position;
            try
            {
                return node.Position;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private double SpeedOf(AudioGraphPlaybackSource source, MediaSourceAudioInputNode node)
        {
            if (_relocations.TryGetValue(source, out var relocation))
            {
                lock (relocation) return relocation.Voice?.Speed ?? relocation.Speed;
            }

            if (_decodedVoices.TryGetValue(source, out var voice)) return voice.Speed;
            try
            {
                return node.PlaybackSpeedFactor;
            }
            catch
            {
                return 1;
            }
        }

        private bool Detached(AudioGraphPlaybackSource source)
        {
            if (source == null || !_relocations.TryGetValue(source, out var relocation)) return false;
            lock (relocation) return relocation.Detached && relocation.Voice == null;
        }

        private void Route(AudioGraphPlaybackSource source, MediaSourceAudioInputNode node, Action<MediaSourceAudioInputNode> onNode,
            Action<DecodedVoice> onVoice, Action<Relocation> onPending)
        {
            if (source != null && _relocations.TryGetValue(source, out var relocation))
            {
                lock (relocation)
                {
                    if (relocation.Voice != null) onVoice(relocation.Voice);
                    else if (relocation.Detached) onPending(relocation);
                    else onNode(node);
                }

                return;
            }

            if (source != null && _decodedVoices.TryGetValue(source, out var voice)) onVoice(voice);
            else onNode(node);
        }

        private static TimeSpan SafeDuration(MediaSourceAudioInputNode node)
        {
            try
            {
                return node.Duration;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }
        #endregion

        #region Playback Source Management
        public async Task ConnectPlaybackSourceAsync(IPlaybackSource playbackSource, PlaybackOptions options = null, CancellationToken cancellationToken = default)
        {
            ThrowExceptionIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            options ??= new PlaybackOptions();

            // 验证播放源类型
            if (playbackSource is not AudioGraphPlaybackSource source)
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");

            // 检查是否已连接
            if (_audioInputNodes.ContainsKey(source))
                throw new ArgumentException("PlaybackSource has been connected to the player.");

            if (_defaultPlayer == null) return;

            // 确保播放源已创建
            if (source.PlaybackSource == null)
                await playbackSource.CreatePlaybackSource();
            cancellationToken.ThrowIfCancellationRequested();

            DecodedVoice voice = null;
            if (options.PreservePitch && Math.Abs(options.Speed - 1) > 0.001 && TimeStretcher.IsAvailable)
            {
                try
                {
                    voice = await DecodedVoice.CreateAsync(_defaultPlayer, source.PlaybackSource, options.Speed, true);
                }
                catch
                {
                    voice = null;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    if (voice != null) DisposeDecodedVoice(voice, null);
                    throw new OperationCanceledException(cancellationToken);
                }
            }

            MediaSourceAudioInputNode node;
            if (voice != null)
            {
                node = voice.Node;
            }
            else
            {
                // 创建音频输入节点
                var nodeResult = await _defaultPlayer.CreateMediaSourceAudioInputNodeAsync(source.PlaybackSource);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (nodeResult.Node is { } abandoned) DisposeInBackground(abandoned);
                    throw new OperationCanceledException(cancellationToken);
                }

                if (nodeResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                    throw nodeResult.ExtendedError;

                node = nodeResult.Node;
                if (Math.Abs(options.Speed - 1) > 0.001) node.PlaybackSpeedFactor = options.Speed;
            }

            // 配置节点
            var gainState = new PlaybackSourceGainState(options.Volume, options.AudioGain);
            var bus = _defaultPlayer.CreateSubmixNode();
            bus.OutgoingGain = gainState.GetOutgoingGain();
            bus.AddOutgoingConnection(_outputNode);
            ConnectFrameOutput(bus);
            if (voice != null)
            {
                voice.Render.AddOutgoingConnection(bus);
                voice.Completed += OnVoiceCompleted;
            }
            else
            {
                node.AddOutgoingConnection(bus);
                node.MediaSourceCompleted += OnMediaSourceCompleted;
            }

            // 注册节点
            _voiceBuses.TryAdd(source, bus);
            if (voice != null) _decodedVoices.TryAdd(source, voice);
            _audioInputNodes.TryAdd(source, node);
            _audioInputNodesReverseDictionary.TryAdd(node, source);
            _playbackSourceGainStates.TryAdd(source, gainState);
            if (voice != null)
                voice.Start(TimeSpan.Zero, bus, _outputNode);
            else if (options.PreservePitch && Math.Abs(options.Speed - 1) > 0.001 && TimeStretcher.CanTranspose)
                AttachPitchCorrector(source, node, options.Speed);

            // 设置为主播放源
            if (_audioInputNodes.Count == 1 || options.SetAsPrimarySource)
                PrimaryPlaybackSource = playbackSource;

            // 根据选项设置播放状态
            if (!options.AutoPlay)
            {
                if (voice == null) node.Stop();
                source.PlaybackStatus = PlaybackStatus.Paused;
            }
            else
            {
                voice?.Play();
                if (GlobalPlaybackStatus is not PlaybackStatus.Playing)
                {
                    PlayAll();
                }
            }
        }

        public void DisconnectPlaybackSource(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            if (playbackSource is not AudioGraphPlaybackSource source)
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");

            if (!_audioInputNodes.TryGetValue(source, out MediaSourceAudioInputNode node)) return;

            CancelRamp(source);

            // Stop first: even if a later detach step fails, the source must become silent
            // before ownership can be retried by the caller.
            try
            {
                Route(source, node, n => n.Stop(), v => v.Pause(), r => r.Playing = false);
                source.PlaybackStatus = PlaybackStatus.Paused;
            }
            catch
            {
                // Continue through disconnect. A successful node disposal is the authoritative
                // indication that cleanup completed.
            }

            UnsubscribeCompletion(node);
            DetachPitchCorrector(source, node);
            DetachTaps(source);
            DetachDecodedVoice(source);
            ReleaseVoice(source, node);

            if (PrimaryPlaybackSource == source)
                PrimaryPlaybackSource = null;

            source.PlaybackStatus = PlaybackStatus.Closed;
            _audioInputNodes.TryRemove(source, out _);
            _audioInputNodesReverseDictionary.TryRemove(node, out _);
            _playbackSourceGainStates.TryRemove(source, out _);

            if (_audioInputNodes.IsEmpty)
            {
                _defaultPlayer.Stop();
                UpdateGlobalPlaybackStatus(PlaybackStatus.Closed);
            }
        }

        public void RemoveAllPlaybackSource()
        {
            ThrowExceptionIfDisposed();
            CancelAllRamps();
            foreach (var source in _audioInputNodes)
            {
                UnsubscribeCompletion(source.Value);
                DetachPitchCorrector(source.Key, source.Value);
                DetachTaps(source.Key);
                DetachDecodedVoice(source.Key);
                ReleaseVoice(source.Key, source.Value);
            }
            _audioInputNodes.Clear();
            _audioInputNodesReverseDictionary.Clear();
            _playbackSourceGainStates.Clear();
            _primaryPlaybackSource = null;
            _defaultPlayer.Stop();
            UpdateGlobalPlaybackStatus(PlaybackStatus.Closed);
        }

        private void UnsubscribeCompletion(MediaSourceAudioInputNode node)
        {
            try
            {
                node.MediaSourceCompleted -= OnMediaSourceCompleted;
            }
            catch
            {
            }
        }

        private void DetachDecodedVoice(AudioGraphPlaybackSource source)
        {
            if (!_decodedVoices.TryRemove(source, out var voice)) return;
            _voiceBuses.TryGetValue(source, out var bus);
            DisposeDecodedVoice(voice, bus);
        }

        private void DisposeDecodedVoice(DecodedVoice voice, AudioSubmixNode bus)
        {
            voice.Completed -= OnVoiceCompleted;
            voice.Dispose();
            var render = voice.Render;
            try
            {
                render.Stop();
                if (bus != null) render.RemoveOutgoingConnection(bus);
            }
            catch
            {
            }

            DisposeInBackground(render);
        }

        private async Task RelocateAsync(AudioGraphPlaybackSource source, Relocation request)
        {
            DecodedVoice voice = null;
            var restore = false;
            await _lifecycleGate.WaitAsync();
            try
            {
                if (_disposedValue || _defaultPlayer == null || source.PlaybackSource == null
                    || !_audioInputNodes.TryGetValue(source, out var node) || !_voiceBuses.TryGetValue(source, out var bus))
                    return;

                DetachTaps(source);
                DetachPitchCorrector(source, node);
                lock (request)
                {
                    request.Position = SafePosition(node);
                    request.Playing = source.PlaybackStatus != PlaybackStatus.Paused;
                    request.Detached = true;
                }

                UnsubscribeCompletion(node);
                try
                {
                    node.Stop();
                    node.RemoveOutgoingConnection(bus);
                }
                catch
                {
                }

                restore = true;
                DisposeInBackground(node);
                source.PlaybackSource.Reset();
                await source.PlaybackSource.OpenAsync();
                voice = await DecodedVoice.CreateAsync(_defaultPlayer, source.PlaybackSource, request.Speed, request.PreservePitch);
                if (voice == null || _disposedValue || !_audioInputNodes.ContainsKey(source) || !_voiceBuses.TryGetValue(source, out bus))
                    return;

                voice.Render.AddOutgoingConnection(bus);
                voice.Completed += OnVoiceCompleted;
                _decodedVoices[source] = voice;
                _audioInputNodesReverseDictionary[voice.Node] = source;
                _audioInputNodes[source] = voice.Node;
                _audioInputNodesReverseDictionary.TryRemove(node, out _);
                lock (request)
                {
                    voice.SetSpeed(request.Speed, request.PreservePitch);
                    voice.Start(request.Position, bus, _outputNode);
                    if (request.Playing) voice.Play();
                    request.Voice = voice;
                }

                voice = null;
                restore = false;
            }
            catch
            {
            }
            finally
            {
                if (voice != null)
                {
                    _decodedVoices.TryRemove(source, out _);
                    _voiceBuses.TryGetValue(source, out var bus);
                    DisposeDecodedVoice(voice, bus);
                }

                if (restore) await RestoreNodeAsync(source, request);
                _relocations.TryRemove(source, out _);
                _lifecycleGate.Release();
            }
        }

        private async Task RestoreNodeAsync(AudioGraphPlaybackSource source, Relocation request)
        {
            try
            {
                if (_disposedValue || _defaultPlayer == null || source.PlaybackSource == null
                    || !_voiceBuses.TryGetValue(source, out var bus) || !_audioInputNodes.TryGetValue(source, out var stale))
                    return;

                source.PlaybackSource.Reset();
                await source.PlaybackSource.OpenAsync();
                var created = await _defaultPlayer.CreateMediaSourceAudioInputNodeAsync(source.PlaybackSource);
                if (created.Status != MediaSourceAudioInputNodeCreationStatus.Success || !_audioInputNodes.ContainsKey(source)) return;
                var node = created.Node;
                node.AddOutgoingConnection(bus);
                node.MediaSourceCompleted += OnMediaSourceCompleted;
                _audioInputNodesReverseDictionary[node] = source;
                _audioInputNodes[source] = node;
                _audioInputNodesReverseDictionary.TryRemove(stale, out _);
                double speed;
                bool preservePitch;
                lock (request)
                {
                    speed = request.Speed;
                    preservePitch = request.PreservePitch;
                    node.PlaybackSpeedFactor = speed;
                    node.Seek(request.Position);
                    if (!request.Playing) node.Stop();
                    request.Detached = false;
                }

                if (preservePitch && Math.Abs(speed - 1) > 0.001 && TimeStretcher.CanTranspose)
                    AttachPitchCorrector(source, node, speed);
            }
            catch
            {
            }
        }

        private static TimeSpan SafePosition(MediaSourceAudioInputNode node)
        {
            try
            {
                return node.Position;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private void ReleaseVoice(AudioGraphPlaybackSource source, MediaSourceAudioInputNode node)
        {
            if (_voiceBuses.TryRemove(source, out var bus))
            {
                try
                {
                    node.RemoveOutgoingConnection(bus);
                }
                catch
                {
                }

                try
                {
                    bus.RemoveOutgoingConnection(_outputNode);
                }
                catch
                {
                }

                try
                {
                    DisconnectFrameOutput(bus);
                }
                catch
                {
                }

                DisposeInBackground(bus);
            }

            DisposeInBackground(node);
        }

        private void ConnectFrameOutput(AudioSubmixNode bus)
        {
            _spectrumFeed.Block();
            try
            {
                bus.AddOutgoingConnection(_frameOutputNode);
            }
            finally
            {
                _spectrumFeed.Unblock();
            }
        }

        private void DisconnectFrameOutput(AudioSubmixNode bus)
        {
            _spectrumFeed.Block();
            try
            {
                bus.RemoveOutgoingConnection(_frameOutputNode);
            }
            finally
            {
                _spectrumFeed.Unblock();
            }
        }

        private static void DisposeInBackground(IDisposable node)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    node.Dispose();
                }
                catch
                {
                }
            });
        }

        public List<AudioGraphPlaybackSource> GetConnectedPlaybackSource()
        {
            ThrowExceptionIfDisposed();
            return _audioInputNodes.Keys.ToList();
        }

        public MediaSourceAudioInputNode GetAudioInputNode(AudioGraphPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            return _audioInputNodes[playbackSource];
        }
        #endregion

        #region Effect Management
        public void AddEffectToPlaybackSource(IAudioEffectDefinition definition, AudioGraphPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            IAudioNode node = GetAudioInputNodeOrThrow(playbackSource);
            if (_decodedVoices.TryGetValue(playbackSource, out var voice)) node = voice.Render;
            if (!node.EffectDefinitions.Contains(definition)) node.EffectDefinitions.Add(definition);
            node.EnableEffectsByDefinition(definition);
        }

        public void RemoveEffectFromPlaybackSource(IAudioEffectDefinition definition, AudioGraphPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            IAudioNode node = GetAudioInputNodeOrThrow(playbackSource);
            if (_decodedVoices.TryGetValue(playbackSource, out var voice)) node = voice.Render;
            node.DisableEffectsByDefinition(definition);
            node.EffectDefinitions.Remove(definition);
        }
        #endregion

        #region Event Handlers
        private void PositionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (PrimaryPlaybackSource is null || GlobalPlaybackStatus is PlaybackStatus.Paused) return;

            var source = PrimaryPlaybackSource as AudioGraphPlaybackSource;
            if (!_audioInputNodes.TryGetValue(source, out var track)) return;

            var position = track == null ? (TimeSpan?)null : PositionOf(source, track);
            if (position == null || position == _lastPosition) return;

            _lastPosition = position.Value;
            OnPositionChanged?.Invoke(position.Value);

            // 更新系统媒体传输控制
            PublishTimeline(position.Value, SafeDuration(track));
        }

        private void OnVoiceCompleted(DecodedVoice voice)
        {
            foreach (var (source, candidate) in _decodedVoices)
            {
                if (!ReferenceEquals(candidate, voice)) continue;
                OnTrackReachesEnd?.Invoke(source);
                return;
            }
        }

        private void OnMediaSourceCompleted(MediaSourceAudioInputNode sender, object args)
        {
            if (_audioInputNodesReverseDictionary.TryGetValue(sender, out var playbackSource))
            {
                OnTrackReachesEnd?.Invoke(playbackSource);
            }
        }
        #endregion

        #region Helper Methods
        private void UpdateGlobalPlaybackStatus(PlaybackStatus status)
        {
            if (GlobalPlaybackStatus == status)
            {
                SMTCManager?.UpdatePlaybackStatus(status);
                return;
            }

            GlobalPlaybackStatus = status;
            SMTCManager?.UpdatePlaybackStatus(status);
            OnGlobalPlaybackStatusChanged?.Invoke(status);
        }

        private MediaSourceAudioInputNode GetAudioInputNodeOrThrow(IPlaybackSource playbackSource)
        {
            if (playbackSource is not AudioGraphPlaybackSource source)
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");

            if (!_audioInputNodes.TryGetValue(source, out var node))
                throw new ArgumentException("PlaybackSource haven't connected to the player.");

            return node;
        }

        private void SetPlaybackSourceGainComponent(
            IPlaybackSource playbackSource,
            double value,
            bool isAudioGain)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            var bus = GetVoiceBusOrThrow(playbackSource);
            var source = (AudioGraphPlaybackSource)playbackSource;
            if (!_playbackSourceGainStates.TryGetValue(source, out var state))
                throw new ArgumentException("PlaybackSource gain state is unavailable.", nameof(playbackSource));

            state.Apply(bus, value, isAudioGain);
        }

        private AudioSubmixNode GetVoiceBusOrThrow(IPlaybackSource playbackSource)
        {
            if (playbackSource is not AudioGraphPlaybackSource source)
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");

            if (!_voiceBuses.TryGetValue(source, out var bus))
                throw new ArgumentException("PlaybackSource haven't connected to the player.");

            return bus;
        }

        private sealed class PlaybackSourceGainState
        {
            private readonly object _syncRoot = new();
            private double _audioGain;
            private double _volume;

            public PlaybackSourceGainState(double volume, double audioGain)
            {
                if (double.IsNaN(volume) || double.IsInfinity(volume) || volume < 0)
                    throw new ArgumentOutOfRangeException(nameof(volume));
                if (double.IsNaN(audioGain) || double.IsInfinity(audioGain) || audioGain < 0)
                    throw new ArgumentOutOfRangeException(nameof(audioGain));

                _volume = volume;
                _audioGain = audioGain;
            }

            public void Apply(IAudioNode node, double value, bool isAudioGain)
            {
                lock (_syncRoot)
                {
                    if (isAudioGain)
                        _audioGain = value;
                    else
                        _volume = value;

                    node.OutgoingGain = _volume * _audioGain;
                }
            }

            public double GetOutgoingGain()
            {
                lock (_syncRoot)
                {
                    return _volume * _audioGain;
                }
            }

            public double GetVolume()
            {
                lock (_syncRoot)
                {
                    return _volume;
                }
            }

            public double GetAudioGain()
            {
                lock (_syncRoot)
                {
                    return _audioGain;
                }
            }
        }

        private void ThrowExceptionIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposedValue, this);
        }
        #endregion
    }
}

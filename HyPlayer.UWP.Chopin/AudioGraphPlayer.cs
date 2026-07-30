using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
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
    public partial class AudioGraphPlayer : IPlayer, IDisposable
    {
        #region Private Fields
        private readonly ConcurrentDictionary<AudioGraphPlaybackSource, MediaSourceAudioInputNode> _audioInputNodes = new();
        private readonly ConcurrentDictionary<MediaSourceAudioInputNode, AudioGraphPlaybackSource> _audioInputNodesReverseDictionary = new();
        private AudioGraph _defaultPlayer;
        private AudioDeviceOutputNode _outputNode;
        private AudioFrameOutputNode _frameOutputNode;
        private bool _disposedValue;
        private readonly Timer _positionTimer = new() { AutoReset = true, Interval = 100 };
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private TimeSpan _lastPosition = TimeSpan.Zero;
        private string _currentDeviceId = string.Empty;
        private double _volume = 1;
        private AudioGraphPlaybackSource _primaryPlaybackSource;
        #endregion

        public AudioGraphPlayer()
        {
            _positionTimer.Elapsed += PositionTimer_Elapsed;
        }

        #region Public Properties
        public bool PlayerCreated => _defaultPlayer != null;
        public double Volume => _volume;

        public bool IsMuted
        {
            get => _outputNode?.OutgoingGain == 0;
            set
            {
                if (_outputNode == null) return;
                _outputNode.OutgoingGain = value ? 0 : _volume;
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
                return _audioInputNodes.TryGetValue(source, out var node) ? node : null;
            }
        }

        public int ConnectedPlaybackSourceCount => _audioInputNodes.Count;
        public ISMTCManager SMTCManager { get; set; }
        public bool EnableFFTProcessing { get; set; } = false;

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

        public delegate void TrackReachesEndHandler(IPlaybackSource source);
        public event TrackReachesEndHandler OnTrackReachesEnd;

        public delegate void PlaybackSourceStatusChangeHandler(IPlaybackSource source, PlaybackStatus status);
        public event PlaybackSourceStatusChangeHandler OnPlaybackSourceStatusChanged;

        public delegate void GlobalPlaybackStatusChangeHandler(PlaybackStatus status);
        public event GlobalPlaybackStatusChangeHandler OnGlobalPlaybackStatusChanged;

        public delegate void PrimaryPlaybackSourceChangeHandler(IPlaybackSource source);
        public event PrimaryPlaybackSourceChangeHandler OnPrimaryPlaybackSourceChanged;
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
                    var setting = await audioGraphSetting.GetAudioGraphSettingsAsync();
                    var graphResult = await AudioGraph.CreateAsync(setting);
                    if (graphResult.Status != AudioGraphCreationStatus.Success)
                        throw graphResult.ExtendedError;

                    newGraph = graphResult.Graph;
                    var outputResult = await newGraph.CreateDeviceOutputNodeAsync();
                    if (outputResult.Status != AudioDeviceNodeCreationStatus.Success)
                        throw outputResult.ExtendedError;

                    newOutputNode = outputResult.DeviceOutputNode;
                    var encodingProperties = newOutputNode.EncodingProperties.Copy();
                    encodingProperties.ChannelCount = 1;
                    newFrameOutputNode = newGraph.CreateFrameOutputNode(encodingProperties);
                    newOutputNode.OutgoingGain = audioGraphSetting.OutputVolume;

                    _outputNode = newOutputNode;
                    _frameOutputNode = newFrameOutputNode;
                    _currentDeviceId = audioGraphSetting.DefaultDeviceId;
                    EnableFFTProcessing = settings.EnableFFTProcessing;
                    newGraph.QuantumProcessed += GraphOnQuantumProcessed;
                    Volatile.Write(ref _defaultPlayer, newGraph);
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

        private void GraphOnQuantumProcessed(AudioGraph sender, object args)
        {
            using var frame = _frameOutputNode.GetFrame();
            if (!EnableFFTProcessing) return;
            try
            {
                FFTProcessor.ProcessFFT(frame);
            }
            catch
            {
                //Ignore
            }
        }

        public async Task ChangePlayerServiceImplementation(IAudioSettings settings)
        {
            ThrowExceptionIfDisposed();
            if (settings is not AudioGraphAudioSetting audioGraphSetting)
                throw new ArgumentException("Setting is not AudioGraphSetting");

            await _lifecycleGate.WaitAsync();
            try
            {
                if (_currentDeviceId == audioGraphSetting.DefaultDeviceId)
                    return;

                _positionTimer.Stop();
                var oldPlayer = _defaultPlayer;
                var oldOutputNode = _outputNode;
                var oldNodes = _audioInputNodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                try
                {
                    // 创建新的 AudioGraph
                    var setting = await audioGraphSetting.GetAudioGraphSettingsAsync();
                    var newPlayerResult = await AudioGraph.CreateAsync(setting);
                    if (newPlayerResult.Status != AudioGraphCreationStatus.Success)
                        throw newPlayerResult.ExtendedError;
                    var newPlayer = newPlayerResult.Graph;


                    oldPlayer.Stop();
                    oldPlayer.QuantumProcessed -= GraphOnQuantumProcessed;

                    // 创建新的输出节点
                    var deviceNodeCreateResult = await newPlayer.CreateDeviceOutputNodeAsync();
                    if (deviceNodeCreateResult.Status != AudioDeviceNodeCreationStatus.Success)
                        throw deviceNodeCreateResult.ExtendedError;
                    var newOutputNode = deviceNodeCreateResult.DeviceOutputNode;
                    newOutputNode.OutgoingGain = oldOutputNode.OutgoingGain;

                    var encodingProperties = _outputNode.EncodingProperties.Copy();
                    encodingProperties.ChannelCount = 1;
                    var frameOutputResult = newPlayer.CreateFrameOutputNode(encodingProperties);
                    _frameOutputNode = frameOutputResult;

                    // 转移所有播放源
                    var newNodes = new ConcurrentDictionary<AudioGraphPlaybackSource, MediaSourceAudioInputNode>();
                    var newNodesReverse = new ConcurrentDictionary<MediaSourceAudioInputNode, AudioGraphPlaybackSource>();

                    foreach (var (source, oldNode) in oldNodes)
                    {
                        // 捕获旧节点状态
                        var position = oldNode.Position;
                        var gain = oldNode.OutgoingGain;
                        var factor = oldNode.PlaybackSpeedFactor;
                        var effects = oldNode.EffectDefinitions.ToList();

                        // 清理旧节点
                        oldNode.MediaSourceCompleted -= OnMediaSourceCompleted;
                        oldNode.Dispose();
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

                        // 应用状态
                        newNode.PlaybackSpeedFactor = factor;
                        newNode.OutgoingGain = gain;
                        newNode.AddOutgoingConnection(newOutputNode);
                        newNode.AddOutgoingConnection(_frameOutputNode);
                        newNode.MediaSourceCompleted += OnMediaSourceCompleted;

                        // 应用效果
                        foreach (var effect in effects)
                        {
                            newNode.EnableEffectsByDefinition(effect);
                        }

                        // 恢复位置并开始
                        await Task.Delay(250);
                        newNode.Seek(position);
                        newNode.Start();

                        newNodes[source] = newNode;
                        newNodesReverse[newNode] = source;
                    }

                    // 替换为新图
                    _defaultPlayer = newPlayer;
                    newPlayer.QuantumProcessed += GraphOnQuantumProcessed;
                    EnableFFTProcessing = settings.EnableFFTProcessing;
                    _outputNode = newOutputNode;
                    _audioInputNodes.Clear();
                    foreach (var kvp in newNodes) _audioInputNodes.TryAdd(kvp.Key, kvp.Value);
                    _audioInputNodesReverseDictionary.Clear();
                    foreach (var kvp in newNodesReverse) _audioInputNodesReverseDictionary.TryAdd(kvp.Key, kvp.Value);

                    _currentDeviceId = audioGraphSetting.DefaultDeviceId;
                    if (GlobalPlaybackStatus == PlaybackStatus.Playing) newPlayer.Start();
                }
                finally
                {
                    oldPlayer.Dispose();
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
                _positionTimer?.Dispose();
                _lifecycleGate.Dispose();
            }

            // 清理所有播放源
            foreach (var item in _audioInputNodes.Values)
            {
                item.RemoveOutgoingConnection(_outputNode);
                item.RemoveOutgoingConnection(_frameOutputNode);
                item.Dispose();
            }

            _defaultPlayer?.Dispose();
            _disposedValue = true;
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
        public void PlayAll()
        {
            ThrowExceptionIfDisposed();
            if (_defaultPlayer == null || ConnectedPlaybackSourceCount == 0) return;

            if (GlobalPlaybackStatus != PlaybackStatus.Playing)
                _defaultPlayer.Start();
            UpdateGlobalPlaybackStatus(PlaybackStatus.Playing);
        }

        public void PauseAll()
        {
            ThrowExceptionIfDisposed();
            if (_defaultPlayer == null) return;

            if (GlobalPlaybackStatus != PlaybackStatus.Paused)
                _defaultPlayer.Stop();
            UpdateGlobalPlaybackStatus(PlaybackStatus.Paused);
        }

        public void PlayPlaybackSource(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);

            node.Start();
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

            node.Stop();
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
                node.Seek(TimeSpan.FromMilliseconds(value));
            }
            else
            {
                node.Seek(target);
            }
        }
        #endregion

        #region Volume and Speed Control
        public void SetOutputVolume(double volume)
        {
            ThrowExceptionIfDisposed();
            if (_outputNode != null)
            {
                _outputNode.OutgoingGain = volume;
                _volume = volume;
            }
        }

        public void SetPlaybackSourceOutputVolume(double volume, IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            node.OutgoingGain = volume;
        }

        public void SetPlaybackSourceSpeed(double speed, IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            node.PlaybackSpeedFactor = speed;
        }

        public double GetPlaybackSourceSpeed(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            return node.PlaybackSpeedFactor;
        }
        #endregion

        #region Playback Source Management
        public async Task ConnectPlaybackSourceAsync(IPlaybackSource playbackSource, PlaybackOptions options = null)
        {
            ThrowExceptionIfDisposed();
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

            // 创建音频输入节点
            var nodeResult = await _defaultPlayer.CreateMediaSourceAudioInputNodeAsync(source.PlaybackSource);
            if (nodeResult.Status != MediaSourceAudioInputNodeCreationStatus.Success)
                throw nodeResult.ExtendedError;

            var node = nodeResult.Node;

            // 配置节点
            node.OutgoingGain = options.Volume;
            node.AddOutgoingConnection(_outputNode);
            node.AddOutgoingConnection(_frameOutputNode);
            node.MediaSourceCompleted += OnMediaSourceCompleted;

            // 注册节点
            _audioInputNodes.TryAdd(source, node);
            _audioInputNodesReverseDictionary.TryAdd(node, source);

            // 设置为主播放源
            if (_audioInputNodes.Count == 1 || options.SetAsPrimarySource)
                PrimaryPlaybackSource = playbackSource;

            // 根据选项设置播放状态
            if (!options.AutoPlay)
            {
                node.Stop();
                source.PlaybackStatus = PlaybackStatus.Paused;
            }
            else
            {
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

            // Stop first: even if a later detach step fails, the source must become silent
            // before ownership can be retried by the caller.
            try
            {
                node.Stop();
                source.PlaybackStatus = PlaybackStatus.Paused;
            }
            catch
            {
                // Continue through disconnect. A successful node disposal is the authoritative
                // indication that cleanup completed.
            }

            node.MediaSourceCompleted -= OnMediaSourceCompleted;

            try
            {
                node.RemoveOutgoingConnection(_outputNode);
            }
            catch
            {
                // Disposing the node below still provides the final detach boundary.
            }

            try
            {
                node.RemoveOutgoingConnection(_frameOutputNode);
            }
            catch
            {
                // Disposing the node below still provides the final detach boundary.
            }

            // Keep the dictionaries and primary-source ownership intact if disposal fails,
            // allowing the ticket owner to retry instead of losing the graph node.
            node.Dispose();

            if (PrimaryPlaybackSource == source)
                PrimaryPlaybackSource = null;

            source.PlaybackStatus = PlaybackStatus.Closed;
            _audioInputNodes.TryRemove(source, out _);
            _audioInputNodesReverseDictionary.TryRemove(node, out _);

            if (_audioInputNodes.IsEmpty)
            {
                _defaultPlayer.Stop();
                UpdateGlobalPlaybackStatus(PlaybackStatus.Closed);
            }
        }

        public void RemoveAllPlaybackSource()
        {
            ThrowExceptionIfDisposed();
            foreach (var source in _audioInputNodes)
            {
                source.Value.MediaSourceCompleted -= OnMediaSourceCompleted;
                source.Value.RemoveOutgoingConnection(_outputNode);
                source.Value.RemoveOutgoingConnection(_frameOutputNode);
                source.Value.Dispose();
            }
            _audioInputNodes.Clear();
            _audioInputNodesReverseDictionary.Clear();
            _primaryPlaybackSource = null;
            _defaultPlayer.Stop();
            UpdateGlobalPlaybackStatus(PlaybackStatus.Closed);
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
            var node = GetAudioInputNodeOrThrow(playbackSource);
            node.EnableEffectsByDefinition(definition);
        }

        public void RemoveEffectFromPlaybackSource(IAudioEffectDefinition definition, AudioGraphPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = GetAudioInputNodeOrThrow(playbackSource);
            node.DisableEffectsByDefinition(definition);
        }
        #endregion

        #region Event Handlers
        private void PositionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (PrimaryPlaybackSource is null || GlobalPlaybackStatus is PlaybackStatus.Paused) return;

            var source = PrimaryPlaybackSource as AudioGraphPlaybackSource;
            if (!_audioInputNodes.TryGetValue(source, out var track)) return;

            var position = track?.Position;
            if (position == null || position == _lastPosition) return;

            _lastPosition = position.Value;
            OnPositionChanged?.Invoke(position.Value);

            // 更新系统媒体传输控制
            var positionProperties = new SystemMediaTransportControlsTimelineProperties
            {
                Position = position.Value,
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                EndTime = track.Duration,
                MaxSeekTime = track.Duration
            };
            SMTCManager?.OnPositionChange(positionProperties);
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

        private void ThrowExceptionIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposedValue, this);
        }
        #endregion
    }
}

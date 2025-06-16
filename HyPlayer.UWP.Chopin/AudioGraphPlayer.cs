using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Effects;
using Timer = System.Timers.Timer;

namespace HyPlayer.UWP.Chopin.Abstractions.Models
{
    public class AudioGraphPlayer : IPlayer, IDisposable
    {
        private ConcurrentDictionary<AudioGraphPlaybackSource, MediaSourceAudioInputNode> _audioInputNodes = new ConcurrentDictionary<AudioGraphPlaybackSource, MediaSourceAudioInputNode>();
        private ConcurrentDictionary<MediaSourceAudioInputNode, AudioGraphPlaybackSource> _audioInputNodesReverseDictionary = new ConcurrentDictionary<MediaSourceAudioInputNode,AudioGraphPlaybackSource>();
        private AudioGraph _defaultPlayer;
        private AudioDeviceOutputNode _outputNode;
        private bool disposedValue;
        private Timer PositionTimer = new Timer() { AutoReset = true, Enabled = true, Interval = 100 };
        private TimeSpan _lastPosition = TimeSpan.Zero;


        public bool PlayerCreated => _defaultPlayer != null;
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

        public ISMTCManager SMTCManager { get; set; }
        public double Volume { get => _volume; }
        private double _volume = 1;
        public bool IsMuted
        {
            get => _outputNode?.OutgoingGain == 0;
            set
            {
                if (_outputNode == null) return;
                if (value == true)
                {
                    _outputNode.OutgoingGain = 0;
                }
                else
                {
                    _outputNode.OutgoingGain = _volume;
                }
            }
        }
        public IPlaybackSource PrimaryPlaybackSource
        {
            get => _primaryPlaybackSource;
            set
            {
                var source = value as AudioGraphPlaybackSource;
                _primaryPlaybackSource = source;
                OnPrimaryPlaybackSourceChanged?.Invoke(source);
            }
        }
        private AudioGraphPlaybackSource _primaryPlaybackSource;
        public PlaybackStatus GlobalPlaybackStatus { get; protected set; } = PlaybackStatus.Closed;
        public MediaSourceAudioInputNode PrimaryAudioInputNode
        {
            get
            {
                if (PrimaryPlaybackSource == null)
                    return null;
                else
                {
                    var source = PrimaryPlaybackSource as AudioGraphPlaybackSource;
                    return _audioInputNodes[source];
                }
            }
        }
        public int ConnectedPlaybackSourceCount => _audioInputNodes.Count;

        public async Task ChangePlayerServiceImplementation(IAudioSettings settings)
        {
            ThrowExceptionIfDisposed();
            if (settings is AudioGraphAudioSetting audioGraphSetting)
            {
                var oldPlayer = _defaultPlayer;
                var oldOutputNode = _outputNode;
                var setting = await audioGraphSetting.GetAudioGraphSettingsAsync();
                var newPlayerResult = await AudioGraph.CreateAsync(setting);
                AudioGraph newPlayer;
                if (newPlayerResult.Status == AudioGraphCreationStatus.Success)
                {
                    newPlayer = newPlayerResult.Graph;
                }
                else
                {
                    throw newPlayerResult.ExtendedError;
                }
                oldPlayer.Stop();
                var oldNodes = _audioInputNodes;
                var deviceNodeCreateResult = await newPlayer.CreateDeviceOutputNodeAsync();
                if (deviceNodeCreateResult.Status != AudioDeviceNodeCreationStatus.Success) throw deviceNodeCreateResult.ExtendedError;
                _outputNode = deviceNodeCreateResult.DeviceOutputNode;
                var newNodes = new ConcurrentDictionary<AudioGraphPlaybackSource, MediaSourceAudioInputNode>();
                var newNodesReverse = new ConcurrentDictionary<MediaSourceAudioInputNode, AudioGraphPlaybackSource>();
                foreach (var node in oldNodes)
                {
                    if (node.Key is AudioGraphPlaybackSource audioGraphPlaybackSource)
                    {
                        if (node.Key.PlaybackSource is null) await node.Key.CreatePlaybackSource();
                        node.Key.PlaybackSource.Reset();
                        var createResult = await newPlayer.CreateMediaSourceAudioInputNodeAsync(node.Key.PlaybackSource);
                        if (createResult.Status != MediaSourceAudioInputNodeCreationStatus.Success) throw createResult.ExtendedError;
                        var outputNode = createResult.Node;
                        newNodes[node.Key] = outputNode;
                        newNodesReverse[outputNode] = node.Key;
                        outputNode.Seek(node.Value.Position);
                        outputNode.PlaybackSpeedFactor = node.Value.PlaybackSpeedFactor;
                        outputNode.OutgoingGain = node.Value.OutgoingGain;
                        outputNode.AddOutgoingConnection(_outputNode);
                        foreach (var effect in node.Value.EffectDefinitions)
                        {
                            outputNode.EnableEffectsByDefinition(effect);
                        }
                        if (node.Key.PlaybackStatus == PlaybackStatus.Playing) outputNode.Start();
                    }
                }
                _outputNode.OutgoingGain = oldOutputNode.OutgoingGain;
                _defaultPlayer = newPlayer;
                newPlayer.Start();
                _audioInputNodes = newNodes;
                _audioInputNodesReverseDictionary = newNodesReverse;
                oldOutputNode?.Dispose();
                oldPlayer.Dispose();
            }
            else
            {
                throw new ArgumentException("Setting is not AudioGraphSetting");
            }
        }

        public async Task InitializePlayer(IAudioSettings settings)
        {
            if (settings is AudioGraphAudioSetting audioGraphSetting)
            {
                PositionTimer.Elapsed += PositionTimer_Elapsed;
                var setting = await audioGraphSetting.GetAudioGraphSettingsAsync();
                var newPlayerResult = await AudioGraph.CreateAsync(setting);
                AudioGraph newPlayer;
                if (newPlayerResult.Status == AudioGraphCreationStatus.Success)
                {
                    newPlayer = newPlayerResult.Graph;
                }
                else
                {
                    throw newPlayerResult.ExtendedError;
                }
                _defaultPlayer = newPlayer;
                var createResult = await newPlayer.CreateDeviceOutputNodeAsync();
                if (createResult.Status != AudioDeviceNodeCreationStatus.Success) throw createResult.ExtendedError;
                _outputNode = createResult.DeviceOutputNode;
                _outputNode.OutgoingGain = audioGraphSetting.OutputVolume;
            }
            else
            {
                throw new ArgumentException("Setting is not AudioGraphSetting");
            }
        }

        private void PositionTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (PrimaryPlaybackSource is null || GlobalPlaybackStatus is PlaybackStatus.Paused) return;
            var source = PrimaryPlaybackSource as AudioGraphPlaybackSource;
            var track = _audioInputNodes[source];
            var position = track?.Position;
            if (position != null)
            {
                if (position != _lastPosition)
                {
                    _lastPosition = position.Value;
                    OnPositionChanged?.Invoke(position.Value);
                    var positionProperties = new SystemMediaTransportControlsTimelineProperties();
                    positionProperties.Position = position.Value;
                    positionProperties.StartTime = TimeSpan.Zero;
                    positionProperties.MinSeekTime = TimeSpan.Zero;
                    positionProperties.EndTime = track.EndTime ?? TimeSpan.Zero;
                    positionProperties.MaxSeekTime = track.EndTime ?? TimeSpan.Zero;
                    SMTCManager.OnPositionChange(positionProperties);
                }
            }
        }

        public void PauseAll()
        {
            ThrowExceptionIfDisposed();
            if (_defaultPlayer == null) return;
            _defaultPlayer.Stop();
            GlobalPlaybackStatus = PlaybackStatus.Paused;
            SMTCManager?.OnPauseAll();
            OnGlobalPlaybackStatusChanged?.Invoke(PlaybackStatus.Paused);
        }

        public void PausePlaybackSource(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var source = playbackSource as AudioGraphPlaybackSource;
            if (source != null)
            {
                _audioInputNodes[source].Stop();
                source.PlaybackStatus = PlaybackStatus.Paused;
                OnPlaybackSourceStatusChanged?.Invoke(playbackSource, PlaybackStatus.Paused);
            }
            else
            {
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");
            }
        }

        public void PlayAll()
        {
            ThrowExceptionIfDisposed();
            if (_defaultPlayer == null) return;
            _defaultPlayer.Start();
            GlobalPlaybackStatus = PlaybackStatus.Playing;
            OnGlobalPlaybackStatusChanged?.Invoke(PlaybackStatus.Playing);
            SMTCManager?.OnPlayAll();
        }

        public void PlayPlaybackSource(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var source = playbackSource as AudioGraphPlaybackSource;
            if (source != null)
            {
                _audioInputNodes[source].Start();
                source.PlaybackStatus = PlaybackStatus.Playing;
                OnPlaybackSourceStatusChanged?.Invoke(playbackSource, PlaybackStatus.Playing);
            }
            else
            {
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");
            }
        }

        public void SeekPlaybackSource(TimeSpan target, IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var source = playbackSource as AudioGraphPlaybackSource;
            if (source != null)
            {
                _audioInputNodes[source].Seek(target);
            }
            else
            {
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");
            }
        }

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
            var source = playbackSource as AudioGraphPlaybackSource;
            if (source != null)
            {
                var item = _audioInputNodes[source];
                item.OutgoingGain = volume;
            }
            else
            {
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");
            }
        }

        public void SetPlaybackSourceSpeed(double speed, IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var source = playbackSource as AudioGraphPlaybackSource;
            if (source != null)
            {
                var item = _audioInputNodes[source];
                item.PlaybackSpeedFactor = speed;
            }
            else
            {
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");
            }
        }
        public double GetPlaybackSourceSpeed(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var source = playbackSource as AudioGraphPlaybackSource;
            if (source != null)
            {
                var item = _audioInputNodes[source];
                return item.PlaybackSpeedFactor;
            }
            else
            {
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");
            }
        }
        public async Task ConnectPlaybackSourceAsync(IPlaybackSource playbackSource, PlaybackOptions options = null)
        {
            ThrowExceptionIfDisposed();
            if (options == null)
            {
                options = new PlaybackOptions();
            }
            var source = playbackSource as AudioGraphPlaybackSource;
            if (_audioInputNodes.ContainsKey(source)) throw new ArgumentException("PlaybackSource has been connected to the player.");
            if (source != null)
            {
                if (_defaultPlayer == null) return;
                if (source.PlaybackSource == null) await playbackSource.CreatePlaybackSource();
                var nodeResult = await _defaultPlayer.CreateMediaSourceAudioInputNodeAsync(source.PlaybackSource);
                if (nodeResult.Status != MediaSourceAudioInputNodeCreationStatus.Success) throw nodeResult.ExtendedError;
                _audioInputNodes[source] = nodeResult.Node;
                _audioInputNodesReverseDictionary[nodeResult.Node] = source;
                nodeResult.Node.OutgoingGain = options.Volume;
                if (!options.AutoPlay)
                {
                    nodeResult.Node.Stop();
                    source.PlaybackStatus = PlaybackStatus.Paused;
                }
                else
                {
                    if (GlobalPlaybackStatus == PlaybackStatus.Closed)
                    {
                        PlayAll();
                    }
                }
                nodeResult.Node.AddOutgoingConnection(_outputNode);
                if (_audioInputNodes.Count == 1 || options.SetAsPrimarySource) PrimaryPlaybackSource = playbackSource;
                _audioInputNodes[source].MediaSourceCompleted += OnMediaSourceCompleted;
            }
            else
            {
                throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");
            }

        }

        private void OnMediaSourceCompleted(MediaSourceAudioInputNode sender, object args)
        {
            var playbackSource = _audioInputNodesReverseDictionary[sender];
            OnTrackReachesEnd?.Invoke(playbackSource);
        }

        public void DisconnectPlaybackSource(IPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var source = playbackSource as AudioGraphPlaybackSource;
            if (source != null)
            {
                if (!_audioInputNodes.ContainsKey(source)) return;
                var item = _audioInputNodes[source];
                _audioInputNodes[source].MediaSourceCompleted -= OnMediaSourceCompleted;
                if (PrimaryPlaybackSource == source) PrimaryPlaybackSource = null;
                item.RemoveOutgoingConnection(_outputNode);
                item.Dispose();
                _audioInputNodes.TryRemove(source, out _);
                _audioInputNodesReverseDictionary.TryRemove(item, out _);
            }
            else throw new ArgumentException("PlaybackSource is not AudioGraphPlaybackSource.");
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

        public void AddEffectToPlaybackSource(IAudioEffectDefinition definition, AudioGraphPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = _audioInputNodes[playbackSource];
            node.EnableEffectsByDefinition(definition);
        }

        public void RemoveEffectFromPlaybackSource(IAudioEffectDefinition definition, AudioGraphPlaybackSource playbackSource)
        {
            ThrowExceptionIfDisposed();
            var node = _audioInputNodes[playbackSource];
            node.DisableEffectsByDefinition(definition);
        }

        public void RemoveAllPlaybackSource()
        {
            ThrowExceptionIfDisposed();
            foreach (var source in _audioInputNodes)
            {
                source.Value.MediaSourceCompleted -= OnMediaSourceCompleted;
                source.Value.RemoveOutgoingConnection(_outputNode);
                source.Value.Dispose();
            }
            _audioInputNodes.Clear();
            _audioInputNodesReverseDictionary.Clear();
            _primaryPlaybackSource = null;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {

                }

                foreach (var item in _audioInputNodes.Values)
                {
                    item.RemoveOutgoingConnection(_outputNode);
                    item.Dispose();
                }
                _outputNode?.Dispose();
                _defaultPlayer?.Dispose();
                disposedValue = true;
            }
        }

        ~AudioGraphPlayer()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        public void ThrowExceptionIfDisposed()
        {
            if (disposedValue) throw new ObjectDisposedException(nameof(AudioGraphPlayer));
        }
    }
}

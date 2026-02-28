using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.HyPlayControl;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Audio;

namespace HyPlayer.Classes
{
    public class FadeManager
    {
#nullable enable
        private AudioGraphPlaybackSource? _currentPlaybackSource;
        private AudioGraphPlaybackSource? _nextPlaybackSource;
        private MediaSourceAudioInputNode? _currentPlaybackNode;
        private MediaSourceAudioInputNode? _nextPlaybackNode;
#nullable restore
        private readonly Dictionary<MediaSourceAudioInputNode, double> _initialVolume = [];
        private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
        private readonly SemaphoreSlim _loaderSemaphore = new(1);
        public bool Processing { get; private set; } = false;
        public FadeManager(AudioGraphPlayer player)
        {
            player.OnPositionChanged += Player_OnPositionChanged;
            HyPlayList.OnSongMoveNext += HyPlayList_OnSongMoveNext;
            HyPlayList.OnMediaEnd += HyPlayList_OnMediaEnd;
        }

        private async void HyPlayList_OnMediaEnd(HyPlayItem hpi)
        {
            if (HyPlayList.NowPlayType == PlayMode.SinglePlay || HyPlayList.List.Count <= 1) return;
            var item = _currentPlaybackSource?.PlaybackSource?.CustomProperties["nowPlayingItem"] as HyPlayItem;
            if (hpi == item)
            {
                HyPlayList.Player.DisconnectPlaybackSource(hpi.PlayItem.AudioGraphPlaybackSource);
                _initialVolume.Remove(_currentPlaybackNode);
                item?.PlayItem?.Dispose();
                item?.PlayItem = null;
                _currentPlaybackSource = null;
                _currentPlaybackNode = null;
            }
            else if (item == null)
            {
                HyPlayList.MoveSongPointer();
                var nextItem = HyPlayList.List[HyPlayList.NowPlaying];
                await HyPlayList.LoadMediaSource(nextItem, true, true);
            }
        }

        private async void HyPlayList_OnSongMoveNext()
        {
            if (!Processing) return;
            Processing = false;
            HyPlayList.Player.DisconnectPlaybackSource(_currentPlaybackSource);
            HyPlayList.Player.DisconnectPlaybackSource(_nextPlaybackSource);
            var item = _currentPlaybackSource?.PlaybackSource?.CustomProperties["nowPlayingItem"] as HyPlayItem;
            item.PlayItem?.Dispose();
            item = _nextPlaybackSource?.PlaybackSource?.CustomProperties["nowPlayingItem"] as HyPlayItem;
            item.PlayItem?.Dispose();
            _currentPlaybackSource = null;
            _currentPlaybackNode = null;
            _nextPlaybackSource = null;
            _nextPlaybackNode = null;
            _initialVolume.Clear();
        }

        private void Player_OnPositionChanged(TimeSpan position)
        {
            InitializeFade().SafeFireAndForget();
            if (!Processing)
            {
                return;
            }
            if (_currentPlaybackNode != null) 
            {
                ProcessFadeOut(_currentPlaybackNode);
            }
            if (_nextPlaybackNode != null) 
            {
                ProcessFadeIn(_nextPlaybackNode);
            }
        }

        private async Task InitializeFade()
        {
            await _loaderSemaphore.WaitAsync();
            if (HyPlayList.Player.PrimaryAudioInputNode?.Duration.TotalSeconds - HyPlayList.Player?.PrimaryAudioInputNode?.Position.TotalSeconds <= _setting.CrossFadeTime
                && ((HyPlayList.Player.PrimaryAudioInputNode?.Duration - HyPlayList.Player.PrimaryAudioInputNode?.Position)?.TotalSeconds ?? 0) > 2
                && HyPlayList.NowPlayType != PlayMode.SinglePlay
                && HyPlayList.List.Count > 1
                && !Processing)
            {
                Processing = true;
                var current = (AudioGraphPlaybackSource)HyPlayList.Player.PrimaryPlaybackSource;
                var currentItem = current.PlaybackSource.CustomProperties["nowPlayingItem"] as HyPlayItem;
                _currentPlaybackSource = current;
                var currentNode = HyPlayList.Player.GetAudioInputNode(current);
                _initialVolume[currentNode] = currentItem.Volume ?? 1;
                _currentPlaybackNode = currentNode;
                HyPlayList.MoveSongPointer();
                var nextItem = HyPlayList.List[HyPlayList.NowPlaying];
                await HyPlayList.LoadMediaSource(nextItem, false, false);
                var nextNode= HyPlayList.Player.GetAudioInputNode(nextItem.PlayItem.AudioGraphPlaybackSource);
                _nextPlaybackSource = nextItem.PlayItem.AudioGraphPlaybackSource;
                _initialVolume[nextNode] = nextItem.Volume ?? 1;
                _nextPlaybackNode = nextNode;
            }
            _loaderSemaphore.Release();
        }

        private void ProcessFadeIn(MediaSourceAudioInputNode node)
        {
            try
            {
                if (node == null) return;
                if (HyPlayList.Player.PrimaryPlaybackSource != _nextPlaybackSource)
                {
                    HyPlayList.Player.PrimaryPlaybackSource = _nextPlaybackSource;
                    HyPlayList.Player?.PlayPlaybackSource(_nextPlaybackSource);
                }
                var time = node.Position.TotalSeconds;
                var multiplier = Math.Clamp(time / _setting.CrossFadeTime, 0, 1);
                HyPlayList.Player.SetPlaybackSourceOutputVolume((_setting.EnableAudioGain ? _initialVolume[node] : 1) * multiplier, _nextPlaybackSource);
                
                if (multiplier == 1)
                {
                    Processing = false;
                    _initialVolume.Remove(node);
                    _nextPlaybackSource = null;
                    _nextPlaybackNode = null;
                }
            }
            catch
            {
                //Ignore
            }
        }

        private void ProcessFadeOut(MediaSourceAudioInputNode node)
        {
            try
            {
                if (node == null) return;
                var time = _setting.CrossFadeTime - (node.Duration - node.Position).TotalSeconds;
                var multiplier = Math.Clamp(1 - (time / _setting.CrossFadeTime), 0, 1);
                Debug.WriteLine(multiplier);
                HyPlayList.Player.SetPlaybackSourceOutputVolume((_setting.EnableAudioGain ? _initialVolume[node] : 1) * multiplier, _currentPlaybackSource);
            }
            catch
            {
                //Ignore
            }
        }

        public void ForceStopFadeProcess()
        {
            if (!Processing)
            {
                return;
            }
            var node = _nextPlaybackNode;
            var source = _nextPlaybackSource;
            HyPlayList.Player.DisconnectPlaybackSource(_currentPlaybackSource);
            HyPlayList.Player.SetPlaybackSourceOutputVolume(Common.Setting.EnableAudioGain ? _initialVolume[node] : 1, source);
            Processing = false;
            var item = _currentPlaybackSource.PlaybackSource.CustomProperties["nowPlayingItem"] as HyPlayItem;
            item?.PlayItem?.Dispose();
            _currentPlaybackSource = null;
            _currentPlaybackNode = null;
            _nextPlaybackSource = null;
            _nextPlaybackNode = null;
            _initialVolume.Clear();
        }
    }
}

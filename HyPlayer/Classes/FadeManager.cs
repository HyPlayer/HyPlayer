using HyPlayer.HyPlayControl;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using Windows.Media.Audio;

namespace HyPlayer.Classes
{
    public class FadeManager
    {
#nullable enable
        private Tuple<AudioGraphPlaybackSource, AudioGraphPlaybackSource>? _currentPlayItem;
        private Tuple<MediaSourceAudioInputNode, MediaSourceAudioInputNode>? _currentNode;
#nullable restore
        private Dictionary<AudioGraphPlaybackSource, double> _initialVolume = new Dictionary<AudioGraphPlaybackSource, double>();
        private bool _loading = false;
        public bool FadeProcessing { get; private set; } = false;
        public bool PauseProcessing { get; private set; } = false;
        public FadeManager(AudioGraphPlayer player)
        {
            player.OnPositionChanged += Player_OnPositionChanged;
            HyPlayList.OnSongMoveNext += HyPlayList_OnSongMoveNext;
            HyPlayList.OnMediaEnd += HyPlayList_OnMediaEnd;
        }

        private async void HyPlayList_OnMediaEnd(HyPlayItem hpi)
        {
            if (HyPlayList.NowPlayType == PlayMode.SinglePlay) return;
            if (FadeProcessing)
            {
                if (hpi.PlayItem.AudioGraphPlaybackSource != null)
                {
                    HyPlayList.Player.DisconnectPlaybackSource(hpi.PlayItem.AudioGraphPlaybackSource);
                    var item = hpi.PlayItem.AudioGraphPlaybackSource.PlaybackSource.CustomProperties["nowPlayingItem"] as HyPlayItem;
                    item?.PlayItem?.FreePlaybackResources();
                }
                if (_initialVolume.Count > 0 && _currentPlayItem != null)
                {
                    HyPlayList.Player.SetPlaybackSourceOutputVolume(_initialVolume[_currentPlayItem?.Item2], _currentPlayItem?.Item2);
                }
                _currentPlayItem = null;
                _currentNode = null;
                FadeProcessing = false;
                _initialVolume.Clear();
            }
            else if ((
                _currentPlayItem == null || 
                _currentNode == null || 
                _initialVolume.Count == 0 || 
                _currentPlayItem.Item2 == HyPlayList.Player.PrimaryPlaybackSource) && !FadeProcessing)
            {
                HyPlayList.MoveSongPointer();
                var nextItem = HyPlayList.List[HyPlayList.NowPlaying];
                await HyPlayList.LoadMediaSource(nextItem, true, true);
            }
        }

        private void HyPlayList_OnSongMoveNext()
        {
            var currentPlayItem = _currentPlayItem;
            _currentPlayItem = null;
            if (currentPlayItem != null)
            {
                var keyItem = currentPlayItem.Item1.PlaybackSource?.CustomProperties["nowPlayingItem"] as HyPlayItem;
                var valueItem = currentPlayItem.Item2.PlaybackSource?.CustomProperties["nowPlayingItem"] as HyPlayItem;
                if (keyItem?.PlayItem is not null)
                {
                    var keySource = keyItem?.PlayItem.AudioGraphPlaybackSource;
                    if (keySource != null) 
                    {
                        HyPlayList.Player.DisconnectPlaybackSource(keySource);
                    }
                    keyItem.PlayItem.FreePlaybackResources();
                }
                if (valueItem?.PlayItem is not null)
                {
                    var valueSource = valueItem?.PlayItem.AudioGraphPlaybackSource;
                    if(valueSource != null)
                    {
                        HyPlayList.Player.DisconnectPlaybackSource(valueSource);
                    }
                    valueItem.PlayItem.FreePlaybackResources();
                }
            }
            _currentNode = null;
            FadeProcessing = false;
            _initialVolume.Clear();
        }

        private async void ShouldStartFade()
        {
            if (HyPlayList.Player.PrimaryAudioInputNode?.Duration.TotalSeconds - HyPlayList.Player?.PrimaryAudioInputNode?.Position.TotalSeconds <= Common.Setting.CrossFadeTime
                && ((HyPlayList.Player.PrimaryAudioInputNode?.Duration - HyPlayList.Player.PrimaryAudioInputNode?.Position)?.TotalSeconds ?? 0) > 0.5
                && _currentPlayItem == null
                && !_loading
                && HyPlayList.NowPlayType != PlayMode.SinglePlay
                && HyPlayList.List.Count > 1
                && !PauseProcessing)
            {
                _loading = true;
                FadeProcessing = true;
                var current = (AudioGraphPlaybackSource)HyPlayList.Player.PrimaryPlaybackSource;
                var currentItem = current.PlaybackSource.CustomProperties["nowPlayingItem"] as HyPlayItem;
                HyPlayList.MoveSongPointer();
                var nextItem = HyPlayList.List[HyPlayList.NowPlaying];
                await HyPlayList.LoadMediaSource(nextItem, false, false);
                _currentPlayItem = new Tuple<AudioGraphPlaybackSource, AudioGraphPlaybackSource>(current, nextItem.PlayItem.AudioGraphPlaybackSource);
                var node1 = HyPlayList.Player.GetAudioInputNode(_currentPlayItem.Item1);
                var node2 = HyPlayList.Player.GetAudioInputNode(_currentPlayItem.Item2);
                _currentNode = new Tuple<MediaSourceAudioInputNode, MediaSourceAudioInputNode>(node1, node2);
                _initialVolume[_currentPlayItem?.Item1] = currentItem.PlayItem.Volume;
                _initialVolume[_currentPlayItem?.Item2] = nextItem.PlayItem.Volume;
                _loading = false;
            }
        }
        private void ProcessFade()
        {
            try
            {
                if (HyPlayList.Player.PrimaryPlaybackSource == _currentPlayItem?.Item1 && _currentPlayItem?.Item2 != null && _currentPlayItem?.Item1 != null)
                {
                    HyPlayList.Player.PrimaryPlaybackSource = _currentPlayItem?.Item2;
                    HyPlayList.Player?.PlayPlaybackSource(_currentPlayItem?.Item2);
                }
                else if (_currentPlayItem?.Item1 != null && _currentPlayItem?.Item2 != null)
                {

                    var time1 = Common.Setting.CrossFadeTime - (_currentNode.Item1.Duration - _currentNode.Item1.Position).TotalSeconds;
                    var time2 = _currentNode.Item2.Duration.TotalSeconds;
                    var time = Math.Min(time1, time2);
                    var mainMultiplier = Math.Min(1, 1 - (time / Common.Setting.CrossFadeTime));
                    var subMultiplier = Math.Max(time / Common.Setting.CrossFadeTime, 0);
                    HyPlayList.Player.SetPlaybackSourceOutputVolume((Common.Setting.EnableAudioGain ? _initialVolume[_currentPlayItem?.Item1] : 1) * mainMultiplier, _currentPlayItem?.Item1);
                    HyPlayList.Player.SetPlaybackSourceOutputVolume((Common.Setting.EnableAudioGain ? _initialVolume[_currentPlayItem?.Item2] : 1) * subMultiplier, _currentPlayItem?.Item2);
                }
            }
            catch
            {
                //Ignore
            }
        }

        private void Player_OnPositionChanged(TimeSpan position)
        {
            ShouldStartFade();
            if (_currentPlayItem == null || _loading || PauseProcessing)
            {
                return;
            }
            else
            {
                ProcessFade();
            }
        }
        public void PauseFadeProcessing()
        {
            if (!FadeProcessing)
            {
                return;
            }
            HyPlayList.Player.SetPlaybackSourceOutputVolume(Common.Setting.EnableAudioGain ? _initialVolume[_currentPlayItem?.Item1] : 1, _currentPlayItem?.Item1);
            HyPlayList.Player.SetPlaybackSourceOutputVolume(Common.Setting.EnableAudioGain ? _initialVolume[_currentPlayItem?.Item2] : 1, _currentPlayItem?.Item2);
            PauseProcessing = true;
            HyPlayList.Player.DisconnectPlaybackSource(_currentPlayItem.Item1);
            var keyItem = _currentPlayItem.Item1.PlaybackSource?.CustomProperties["nowPlayingItem"] as HyPlayItem;
            keyItem.PlayItem.FreePlaybackResources();
            _currentPlayItem = null;
            _currentNode = null;
            FadeProcessing = false;
            _initialVolume.Clear();
        }
        public void ResumeFadeProcessing()
        {
            PauseProcessing = false;
        }
    }
}

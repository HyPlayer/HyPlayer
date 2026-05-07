using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace HyPlayer.ViewModels
{
    public partial class CompactPlayerViewModel : ObservableRecipient
    {
        private readonly IPlaylistService _playlistService;
        private readonly IPlaybackControlService _playbackControlService;
        private readonly PlaybackStateService _playbackStateService;
        private readonly AudioGraphPlayer _audioGraphPlayer;
        private readonly ILyricService _lyricService;

        [ObservableProperty] private bool _lyricIsKaraokeLyric;
        [ObservableProperty] private SongLyric _lrc;
        [ObservableProperty] private double _nowProgress;
        [ObservableProperty] private double _totalProgress;
            

        public CompactPlayerViewModel
            (IPlaylistService playlistService, IPlaybackControlService playbackControlService, PlaybackStateService playbackStateService, AudioGraphPlayer audioGraphPlayer, ILyricService lyricService)
        {
            _playlistService = playlistService;
            _playbackControlService = playbackControlService;
            _playbackStateService = playbackStateService;
            _audioGraphPlayer = audioGraphPlayer;
            _lyricService = lyricService;
        }


    }
}

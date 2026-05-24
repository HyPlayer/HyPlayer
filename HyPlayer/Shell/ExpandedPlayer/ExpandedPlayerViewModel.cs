using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using CommunityToolkit.WinUI.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Shell.ExpandedPlayer
{
    public partial class ExpandedPlayerViewModel : ObservableObject
    {
        // ── Services ──────────────────────────────────────────────
        private readonly IPlaylistService _playlist;
        private readonly IPlaybackControlService _control;
        private readonly PlaybackStateService _state;
        private readonly ILyricService _lyricService;
        private readonly INotificationService _notification;
        private readonly Setting _settings;
        private readonly IAuthService _authService;
        private readonly WeakEventListener<ExpandedPlayerViewModel, object?, PropertyChangedEventArgs> _stateChangedListener;
        private readonly WeakEventListener<ExpandedPlayerViewModel, object?, SongLikeStatusChangedEventArgs> _songLikeStatusChangedListener;

        public ExpandedPlayerViewModel(
            IPlaylistService playlist,
            IPlaybackControlService control,
            PlaybackStateService state,
            ILyricService lyricService,
            INotificationService notification,
            Setting settings,
            IAuthService authService)
        {
            _playlist = playlist;
            _control = control;
            _state = state;
            _lyricService = lyricService;
            _notification = notification;
            _settings = settings;
            _authService = authService;
            SyncFromState();
            _stateChangedListener = new WeakEventListener<ExpandedPlayerViewModel, object?, PropertyChangedEventArgs>(this)
            {
                OnEventAction = static (instance, _, args) => instance.OnPlaybackStatePropertyChanged(args.PropertyName),
                OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
            };
            _state.PropertyChanged += _stateChangedListener.OnEvent;
            _songLikeStatusChangedListener = new WeakEventListener<ExpandedPlayerViewModel, object?, SongLikeStatusChangedEventArgs>(this)
            {
                OnEventAction = static (instance, _, args) => instance.RunOnUIThread(() => instance.IsLiked = args.IsLiked),
                OnDetachAction = weakEventListener => { _authService.SongLikeStatusChanged -= weakEventListener.OnEvent; }
            };
            _authService.SongLikeStatusChanged += _songLikeStatusChangedListener.OnEvent;
        }

        // ── Observable properties ─────────────────────────────────

        [ObservableProperty]
        public partial HyPlayItem? NowPlayingItem { get; set; }

        [ObservableProperty]
        public partial SingleSongBase? NowPlayingProviderItem { get; set; }

        [ObservableProperty]
        public partial bool IsPlaying { get; set; }

        [ObservableProperty]
        public partial TimeSpan Position { get; set; }

        [ObservableProperty]
        public partial TimeSpan Duration { get; set; }

        [ObservableProperty]
        public partial double Volume { get; set; }

        [ObservableProperty]
        public partial string ActiveStrategyId { get; set; } = "seq";

        [ObservableProperty]
        public partial HyLyricInfo LyricInfo { get; set; } = new();

        [ObservableProperty]
        public partial int LyricIndex { get; set; }

        [ObservableProperty]
        public partial bool IsInFm { get; set; }

        [ObservableProperty]
        public partial string QualityTag { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SongName { get; set; }

        [ObservableProperty]
        public partial string Album { get; set; }

        [ObservableProperty]
        public partial string Artist { get; set; }

        [ObservableProperty]
        public partial BitmapImage Cover { get; set; } = new BitmapImage(new Uri("ms-appx:///Assets/icon.png"));

        [ObservableProperty]
        public partial IReadOnlyList<HyPlayItem> PlaylistItems { get; set; }

        [ObservableProperty]
        public partial bool IsLiked { get; set; }

        [ObservableProperty]
        public partial bool IsPlaylistVisible { get; set; }

        // ── Expose services for code-behind that still needs them ─
        public IPlaylistService Playlist => _playlist;
        public IPlaybackControlService Control => _control;
        public PlaybackStateService State => _state;
        public ILyricService LyricService => _lyricService;
        public Setting Settings => _settings;

        // ── Commands ──────────────────────────────────────────────

        [RelayCommand]
        private void TogglePlayPause() => _control.TogglePlayPause();

        [RelayCommand]
        private async Task MoveNextAsync() => await _playlist.MoveNextAsync(userInitiated: true);

        [RelayCommand]
        private async Task MovePreviousAsync() => await _playlist.MovePreviousAsync();

        [RelayCommand]
        private void ChangePlayMode()
        {
            // Cycle: seq → sgl → shn → seq
            var next = ActiveStrategyId switch
            {
                "seq" => "sgl",
                "sgl" => "shn",
                _ => "seq"
            };
            _playlist.SetStrategy(next);
            ActiveStrategyId = next;
        }

        [RelayCommand]
        private async Task SeekAsync(TimeSpan position) => await _control.SeekAsync(position);

        [RelayCommand]
        private void LikeSong()
        {
            IsLiked = !IsLiked;
            _authService.LikeSong();
        }

        [RelayCommand]
        private void TogglePlaylist() => IsPlaylistVisible = !IsPlaylistVisible;

        private void OnPlaybackStatePropertyChanged(string? propertyName)
        {
            RunOnUIThread(() =>
            {
                switch (propertyName)
                {
                    case nameof(PlaybackStateService.NowPlayingItem):
                        SyncFromState();
                        break;
                    case nameof(PlaybackStateService.NowPlayingProviderItem):
                        SyncFromState();
                        break;
                    case nameof(PlaybackStateService.IsPlaying):
                        IsPlaying = _state.IsPlaying;
                        break;
                    case nameof(PlaybackStateService.LyricInfo):
                        LyricInfo = _state.LyricInfo;
                        break;
                    case nameof(PlaybackStateService.Position):
                        Position = _state.Position;
                        break;
                    case nameof(PlaybackStateService.LyricIndex):
                        LyricIndex = _state.LyricIndex;
                        break;
                }
            });
        }

        /// <summary>
        /// Pull current values from PlaybackStateService into ViewModel properties.
        /// Called on activation and can be called when navigating to the page.
        /// </summary>
        public void SyncFromState()
        {
            NowPlayingItem = _state.NowPlayingItem;
            NowPlayingProviderItem = _state.NowPlayingProviderItem ?? _playlist.NowPlayingProviderItem;
            IsPlaying = _state.IsPlaying;
            Volume = _state.Volume;
            Position = _state.Position;
            Duration = _state.Duration;
            ActiveStrategyId = _state.ActiveStrategyId;
            LyricInfo = _state.LyricInfo;
            LyricIndex = _state.LyricIndex;
            IsInFm = _state.IsInFm;
            QualityTag = _state.QualityTag;
            PlaylistItems = _playlist.Items;

            if (NowPlayingProviderItem != null)
            {
                SongName = NowPlayingProviderItem.Name;
                Album = NowPlayingProviderItem.Album?.Name ?? string.Empty;
                Artist = NowPlayingProviderItem.CreatorList is { Count: > 0 } creators
                    ? string.Join("; ", creators)
                    : string.Empty;
            }
            else if (NowPlayingItem != null)
            {
                SongName = NowPlayingItem.Name;
                Album = NowPlayingItem.AlbumString;
                Artist = NowPlayingItem.ArtistString;
            }
            else
            {
                SongName = string.Empty;
                Album = string.Empty;
                Artist = string.Empty;
            }
        }

        private void RunOnUIThread(Action action)
        {
            _ = _notification.InvokeOnUIThread(action);
        }
    }
}

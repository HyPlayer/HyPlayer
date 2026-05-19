using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
using System.Diagnostics.CodeAnalysis;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.ViewModels
{
    public partial class ExpandedPlayerViewModel : ObservableRecipient
    {
        // ── Services ──────────────────────────────────────────────
        private readonly IPlaylistService _playlist;
        private readonly IPlaybackControlService _control;
        private readonly PlaybackStateService _state;
        private readonly ILyricService _lyricService;
        private readonly INotificationService _notification;
        private readonly Setting _settings;

        public ExpandedPlayerViewModel(
            IPlaylistService playlist,
            IPlaybackControlService control,
            PlaybackStateService state,
            ILyricService lyricService,
            INotificationService notification,
            Setting settings)
        {
            _playlist = playlist;
            _control = control;
            _state = state;
            _lyricService = lyricService;
            _notification = notification;
            _settings = settings;

            // Activate messenger registrations
            IsActive = true;
        }

        // ── Observable properties ─────────────────────────────────

        [ObservableProperty]
        public partial HyPlayItem? NowPlayingItem { get; set; }

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
            // Actual API call is handled by the like service via messenger
        }

        [RelayCommand]
        private void TogglePlaylist() => IsPlaylistVisible = !IsPlaylistVisible;

        // ── Messenger registrations ───────────────────────────────
        [RequiresUnreferencedCode("This method requires the generated CommunityToolkit.Mvvm.Messaging.__Internals.__IMessengerExtensions type not to be removed to use the fast path. If this type is removed by the linker, or if the target recipient was created dynamically and was missed by the source generator, a slower fallback path using a compiled LINQ expression will be used. This will have more overhead in the first invocation of this method for any given recipient type. Alternatively, OnActivated() can be manually overwritten, and registration can be done individually for each required message for this recipient.")]
        [RequiresDynamicCode("This method requires the generated CommunityToolkit.Mvvm.Messaging.__Internals.__IMessengerExtensions type not to be removed to use the fast path. If that is present, the method is AOT safe, as the only methods being invoked to register the messages will be the ones produced by the source generator. If it isn't, this method will need to dynamically create the generic methods to register messages, which might not be available at runtime. Alternatively, OnActivated() can be manually overwritten, and registration can be done individually for each required message for this recipient.")]
        protected override void OnActivated()
        {
            Messenger.Register<TrackChangedMessage>(this, (r, _) =>
            {
                var vm = (ExpandedPlayerViewModel)r;
                vm.RunOnUIThread(vm.SyncFromState);
            });

            Messenger.Register<PlaybackStateChangedMessage>(this, (r, m) =>
            {
                var vm = (ExpandedPlayerViewModel)r;
                vm.RunOnUIThread(() => vm.IsPlaying = m.IsPlaying);
            });

            Messenger.Register<LyricLoadedMessage>(this, (r, m) =>
            {
                var vm = (ExpandedPlayerViewModel)r;
                vm.RunOnUIThread(() => vm.LyricInfo = m.Info);
            });

            Messenger.Register<SongLikeStatusChangedMessage>(this, (r, m) =>
            {
                var vm = (ExpandedPlayerViewModel)r;
                vm.RunOnUIThread(() => vm.IsLiked = m.IsLiked);
            });

            // ── High-frequency position: keep direct subscription ──
            Messenger.Register<PositionTickMessage>(this, (r, m) =>
            {
                var vm = (ExpandedPlayerViewModel)r;
                vm.RunOnUIThread(() => vm.Position = m.Position);
            });

            Messenger.Register<LyricIndexChangedMessage>(this, (r, m) =>
            {
                var vm = (ExpandedPlayerViewModel)r;
                vm.RunOnUIThread(() => vm.LyricIndex = m.Index);
            });

            SyncFromState();
        }

        /// <summary>
        /// Pull current values from PlaybackStateService into ViewModel properties.
        /// Called on activation and can be called when navigating to the page.
        /// </summary>
        public void SyncFromState()
        {
            NowPlayingItem = _state.NowPlayingItem;
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

            if (NowPlayingItem != null)
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

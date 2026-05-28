using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Lyrics.LyricParser.Abstraction;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Shell.CompactPlayer;

public partial class CompactPlayerViewModel : ObservableObject
{
    private readonly Setting _setting;
    private readonly IAuthService _authService;
    private readonly IPlaylistService _playlist;
    private readonly IPlaybackControlService _control;
    private readonly PlaybackStateService _state;
    private readonly ILyricService _lyricService;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly WeakEventListener<CompactPlayerViewModel, object?, PropertyChangedEventArgs> _stateChangedListener;
    private readonly WeakEventListener<CompactPlayerViewModel, object?, SongLikeStatusChangedEventArgs> _songLikeStatusChangedListener;

    public CompactPlayerViewModel(
        Setting setting,
        IAuthService authService,
        IPlaylistService playlist,
        IPlaybackControlService control,
        PlaybackStateService state,
        ILyricService lyricService,
        IBackgroundTaskRunner taskRunner)
    {
        _setting = setting;
        _authService = authService;
        _playlist = playlist;
        _control = control;
        _state = state;
        _lyricService = lyricService;
        _taskRunner = taskRunner;

        _stateChangedListener = new WeakEventListener<CompactPlayerViewModel, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnPlaybackStatePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _state.PropertyChanged += _stateChangedListener.OnEvent;
        _songLikeStatusChangedListener = new WeakEventListener<CompactPlayerViewModel, object?, SongLikeStatusChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => { instance.IsLiked = args.IsLiked; },
            OnDetachAction = weakEventListener => { _authService.SongLikeStatusChanged -= weakEventListener.OnEvent; }
        };
        _authService.SongLikeStatusChanged += _songLikeStatusChangedListener.OnEvent;
    }

    [ObservableProperty]
    public partial double NowProgress { get; set; }

    [ObservableProperty]
    public partial double TotalProgress { get; set; }

    [ObservableProperty]
    public partial string LyricText { get; set; } = "小窗模式";

    [ObservableProperty]
    public partial string LyricTranslation { get; set; } = "将鼠标移到窗口以查看更多功能";

    [ObservableProperty]
    public partial string LyricSound { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NowPlayingName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NowPlayingArtists { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsLiked { get; set; }

    [ObservableProperty]
    public partial SongLyric? CurrentLyric { get; set; }

    [ObservableProperty]
    public partial bool LyricQuickRenderMode { get; set; }
    [ObservableProperty]
    public partial BitmapImage AlbumImage { get; set; }

    public string PlayStateGlyph => IsPlaying ? "\uF8AE" : "\uF5B0";
    public string LikeIconGlyph => IsLiked ? "\uE00B" : "\uE006";
    public Brush? LikeIconForeground => IsLiked
        ? new SolidColorBrush(Colors.Red)
        : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayStateGlyph));

    partial void OnIsLikedChanged(bool value)
    {
        OnPropertyChanged(nameof(LikeIconGlyph));
        OnPropertyChanged(nameof(LikeIconForeground));
    }

    [RelayCommand]
    private async Task MovePreviousAsync()
    {
        await _playlist.MovePreviousAsync();
    }

    [RelayCommand]
    private async Task MoveNextAsync()
    {
        await _playlist.MoveNextAsync(true);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        _control.TogglePlayPause();
    }

    [RelayCommand]
    private void ToggleCompactBlur()
    {
        _setting.CompactPlayerPageBlurStatus = !_setting.CompactPlayerPageBlurStatus;
    }

    [RelayCommand]
    private void LikeSong()
    {
        _authService.LikeSong();
    }

    [RelayCommand]
    private void ExitCompactOverlay()
    {
        _taskRunner.Forget(ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default).AsTask(),
            "exit compact overlay mode");
    }

    public void Detach()
    {
        _stateChangedListener.Detach();
        _songLikeStatusChangedListener.Detach();
    }

    public void SyncFromState()
    {
        RunOnUIThread(() => { IsPlaying = _state.IsPlaying; });
        OnChangePlayItem(_state.NowPlayingItem);
        OnPlayPositionChanged(_state.Position);
        OnLyricChanged();
        OnSongCoverChanged();
    }

    private async void OnSongCoverChanged()
    {
        if (_state.CoverStream == null) return;
        RunOnUIThread(async () =>
        {
            using var stream = _state.CoverStream.CloneStream();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            AlbumImage = bitmap;
        });
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(PlaybackStateService.Position):
                OnPlayPositionChanged(_state.Position);
                break;
            case nameof(PlaybackStateService.NowPlayingItem):
                OnChangePlayItem(_state.NowPlayingItem);
                break;
            case nameof(PlaybackStateService.LyricIndex):
                OnLyricChanged();
                break;
            case nameof(PlaybackStateService.IsPlaying):
                RunOnUIThread(() => IsPlaying = _state.IsPlaying);
                break;
            case nameof(PlaybackStateService.CoverStream):
                OnSongCoverChanged();
                break;
        }
    }

    private void OnPlayPositionChanged(TimeSpan position)
    {
        RunOnUIThread(() => { NowProgress = position.TotalMilliseconds; });
    }

    private void OnChangePlayItem(HyPlayItem? item)
    {
        RunOnUIThread(() => 
        {
            NowPlayingName = item?.Name;
            NowPlayingArtists = item?.ArtistString;
            TotalProgress = item?.LengthInMilliseconds ?? 0;

            if (item is null)
                return;

            if (item.ItemType is not (HyPlayItemType.Local or HyPlayItemType.LocalProgressive))
            {
                var liked = _authService.LikedSongs.Contains(_state.NowPlayingItem?.Id);
                IsLiked = liked;
            }
            else
            {
                IsLiked = false;
            }
        });
    }

    private void OnLyricChanged()
    {
        if (_lyricService.CurrentLyricIndex == -1) return;
        if (_lyricService.CurrentLyricInfo.Lyrics.Count <= _lyricService.CurrentLyricIndex) return;

        try
        {
            RunOnUIThread(() => {
                if (_lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex].LyricLine is KaraokeLyricsLine kara)
                {
                    LyricQuickRenderMode = false;
                    if (kara.Duration.TotalSeconds > 1)
                    {
                        return;
                    }
                }
                else if (_lyricService.CurrentLyricIndex < _lyricService.CurrentLyricInfo.Lyrics.Count - 1 &&
                         _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex + 1].LyricLine is LrcLyricsLine lrcLine)
                {
                    if (lrcLine.StartTime.TotalSeconds -
                        _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex].LyricLine.StartTime.TotalSeconds > 1)
                    {
                        LyricQuickRenderMode = false;
                        return;
                    }
                    LyricQuickRenderMode = true;
                }
            });
        }
        finally
        {
            ChangeLyric();
        }
    }

    private void ChangeLyric()
    {
        RunOnUIThread(() =>
        {
            CurrentLyric = _lyricService.CurrentLyricInfo.Lyrics[_lyricService.CurrentLyricIndex];
            LyricText = CurrentLyric.LyricLine.CurrentLyric;
            LyricTranslation = CurrentLyric.Translation;
            LyricSound = CurrentLyric.Romaji;
        });
    }
    private void RunOnUIThread(Action action)
    {
        _taskRunner.Forget(CoreApplication.MainView.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { action(); }), "PlayBar UI update");
    }
}

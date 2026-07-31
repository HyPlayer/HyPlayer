using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Application.Threading;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Shell.ExpandedPlayer;

public partial class ExpandedPlayerViewModel : ObservableObject, IDisposable
{
    private readonly IAuthService _authService;

    // ── Services ──────────────────────────────────────────────
    private readonly WeakEventListener<ExpandedPlayerViewModel, object?, SongLikeStatusChangedEventArgs>
        _songLikeStatusChangedListener;

    private readonly WeakEventListener<ExpandedPlayerViewModel, object?, PropertyChangedEventArgs>
        _stateChangedListener;

    private readonly IUIThreadDispatcher _uiThreadDispatcher;
    private bool _disposedValue;

    public ExpandedPlayerViewModel(
        PlayCoreBase playCore,
        IPlaybackControlService control,
        PlaybackStateService state,
        ILyricService lyricService,
        IUIThreadDispatcher uiThreadDispatcher,
        PlaybackSettings playbackSettings,
        UISettings uiSettings,
        LyricSettings lyricSettings,
        IAuthService authService)
    {
        PlayCore = playCore;
        Control = control;
        State = state;
        LyricService = lyricService;
        _uiThreadDispatcher = uiThreadDispatcher;
        PlaybackSettings = playbackSettings;
        UISettings = uiSettings;
        LyricSettings = lyricSettings;
        _authService = authService;
        SyncFromState();
        _stateChangedListener = new WeakEventListener<ExpandedPlayerViewModel, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnPlaybackStatePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { State.PropertyChanged -= weakEventListener.OnEvent; }
        };
        State.PropertyChanged += _stateChangedListener.OnEvent;
        _songLikeStatusChangedListener =
            new WeakEventListener<ExpandedPlayerViewModel, object?, SongLikeStatusChangedEventArgs>(this)
            {
                OnEventAction = static (instance, _, args) =>
                    instance.RunOnUIThread(() => instance.IsLiked = args.IsLiked),
                OnDetachAction = weakEventListener =>
                {
                    _authService.SongLikeStatusChanged -= weakEventListener.OnEvent;
                }
            };
        _authService.SongLikeStatusChanged += _songLikeStatusChangedListener.OnEvent;
    }

    // ── Observable properties ─────────────────────────────────

    [ObservableProperty] public partial SingleSongBase? NowPlayingProviderItem { get; set; }

    [ObservableProperty] public partial PlaybackCurrentItemSnapshot? NowPlayingSnapshot { get; set; }

    [ObservableProperty] public partial bool IsPlaying { get; set; }

    [ObservableProperty] public partial TimeSpan Position { get; set; }

    [ObservableProperty] public partial TimeSpan Duration { get; set; }

    [ObservableProperty] public partial double Volume { get; set; }

    [ObservableProperty] public partial string ActiveStrategyId { get; set; } = "seq";

    [ObservableProperty] public partial HyLyricInfo LyricInfo { get; set; } = new();

    [ObservableProperty] public partial int LyricIndex { get; set; }

    [ObservableProperty] public partial bool IsInFm { get; set; }

    [ObservableProperty] public partial string QualityTag { get; set; } = string.Empty;

    [ObservableProperty] public partial string SongName { get; set; }

    [ObservableProperty] public partial string Album { get; set; }

    [ObservableProperty] public partial string Artist { get; set; }

    [ObservableProperty] public partial BitmapImage Cover { get; set; } = new(new Uri("ms-appx:///Assets/icon.png"));

    [ObservableProperty] public partial bool IsLiked { get; set; }

    [ObservableProperty] public partial bool IsPlaylistVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackAccentBrush))]
    public partial PlaybackThemeSnapshot DisplayedTheme { get; set; } = PlaybackThemeSnapshot.Default;

    public SolidColorBrush PlaybackAccentBrush => DisplayedTheme.AccentBrush;

    // ── Expose services for code-behind that still needs them ─
    public PlayCoreBase PlayCore { get; }

    public IPlaybackControlService Control { get; }

    public PlaybackStateService State { get; }

    public ILyricService LyricService { get; }

    public PlaybackSettings PlaybackSettings { get; }

    public UISettings UISettings { get; }

    public LyricSettings LyricSettings { get; }

    // ── Commands ──────────────────────────────────────────────

    [RelayCommand]
    private void TogglePlayPause()
    {
        Control.TogglePlayPause();
    }

    [RelayCommand]
    private async Task MoveNextAsync()
    {
        await Control.MoveNextAndPlayAsync(true);
    }

    [RelayCommand]
    private async Task MovePreviousAsync()
    {
        await Control.MovePreviousAndPlayAsync();
    }

    [RelayCommand]
    private async Task ChangePlayMode()
    {
        // Cycle: seq → sgl → shn → seq
        var next = ActiveStrategyId switch
        {
            "seq" => "sgl",
            "sgl" => "shn",
            _ => "seq"
        };
        await Control.SetPlayModeAsync(next);
        PlaybackSettings.ActiveStrategyId = next;
        ActiveStrategyId = next;
    }

    [RelayCommand]
    private async Task SeekAsync(TimeSpan position)
    {
        await Control.SeekAsync(position);
    }

    [RelayCommand]
    private void LikeSong()
    {
        IsLiked = !IsLiked;
        _authService.LikeSong();
    }

    [RelayCommand]
    private void TogglePlaylist()
    {
        IsPlaylistVisible = !IsPlaylistVisible;
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        RunOnUIThread(() =>
        {
            switch (propertyName)
            {
                case nameof(PlaybackStateService.NowPlayingProviderItem):
                case nameof(PlaybackStateService.NowPlayingSnapshot):
                    SyncFromState();
                    break;
                case nameof(PlaybackStateService.IsPlaying):
                    IsPlaying = State.IsPlaying;
                    break;
                case nameof(PlaybackStateService.LyricInfo):
                    LyricInfo = State.LyricInfo;
                    break;
                case nameof(PlaybackStateService.Position):
                    Position = State.Position;
                    break;
                case nameof(PlaybackStateService.LyricIndex):
                    LyricIndex = State.LyricIndex;
                    break;
            }
        });
    }

    /// <summary>
    ///     Pull current values from PlaybackStateService into ViewModel properties.
    ///     Called on activation and can be called when navigating to the page.
    /// </summary>
    public void SyncFromState()
    {
        NowPlayingProviderItem = State.NowPlayingProviderItem ?? PlayCore.CurrentSong;
        NowPlayingSnapshot =
            State.NowPlayingSnapshot ?? PlaybackCurrentItemSnapshot.FromProvider(NowPlayingProviderItem);
        IsPlaying = State.IsPlaying;
        Volume = State.Volume;
        Position = State.Position;
        Duration = State.Duration;
        ActiveStrategyId = State.ActiveStrategyId;
        LyricInfo = State.LyricInfo;
        LyricIndex = State.LyricIndex;
        IsInFm = State.IsInFm;
        QualityTag = State.QualityTag;
        if (NowPlayingSnapshot != null)
        {
            SongName = NowPlayingSnapshot.Name;
            Album = NowPlayingSnapshot.AlbumName;
            Artist = NowPlayingSnapshot.ArtistText;
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
        _ = _uiThreadDispatcher.TryRunAsync(action);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _stateChangedListener.Detach();
                _songLikeStatusChangedListener.Detach();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

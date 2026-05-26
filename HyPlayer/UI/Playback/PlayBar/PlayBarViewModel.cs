using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using CommunityToolkit.WinUI.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace HyPlayer.UI.Playback.PlayBar;

public partial class PlayBarViewModel : ObservableObject
{
    private readonly IPlaylistService _playlist;
    private readonly IPlaybackControlService _control;
    private readonly PlaybackStateService _state;
    private readonly ILyricService _lyricService;
    private readonly Setting _setting;
    private readonly INotificationService _notification;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly IAuthService _authService;
    private readonly DataTransferManager _dataTransferManager;
    private readonly WeakEventListener<PlayBarViewModel, object?, PropertyChangedEventArgs> _stateChangedListener;
    private readonly WeakEventListener<PlayBarViewModel, object?, PlaylistChangedEventArgs> _playlistChangedListener;

    public PlayBarViewModel(
        IPlaylistService playlist,
        IPlaybackControlService control,
        PlaybackStateService state,
        ILyricService lyricService,
        Setting setting,
        INotificationService notification,
        IBackgroundTaskRunner taskRunner,
        IAuthService authService)
    {
        _playlist = playlist;
        _control = control;
        _state = state;
        _lyricService = lyricService;
        _setting = setting;
        _notification = notification;
        _taskRunner = taskRunner;
        _authService = authService;
        _dataTransferManager = DataTransferManager.GetForCurrentView();
        SyncFromState();
        _stateChangedListener = new WeakEventListener<PlayBarViewModel, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnPlaybackStatePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _state.PropertyChanged += _stateChangedListener.OnEvent;
        _playlistChangedListener = new WeakEventListener<PlayBarViewModel, object?, PlaylistChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.OnPlaylistChanged(),
            OnDetachAction = weakEventListener => { _playlist.PlaylistChanged -= weakEventListener.OnEvent; }
        };
        _playlist.PlaylistChanged += _playlistChangedListener.OnEvent;
    }

    // ── Observable Properties (partial property pattern for AOT) ──

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
    public partial int LyricIndex { get; set; }

    [ObservableProperty]
    public partial HyLyricInfo LyricInfo { get; set; } = new();

    [ObservableProperty]
    public partial bool IsInFm { get; set; }

    [ObservableProperty]
    public partial string QualityTag { get; set; } = string.Empty;

    /// <summary>
    /// Observable playlist items for the ListBox binding.
    /// </summary>
    public ObservableCollection<HyPlayItem> PlaylistItems { get; } = [];

    // ── Playlist service pass-through ──

    public int QueueCount => _playlist.QueueCount;
    public int NowPlayingIndex => _playlist.NowPlayingIndex;
    public string PlaySourceId => _playlist.PlaySourceId;

    /// <summary>
    /// Pass-through to PlaybackStateService.CoverStream for UI cover loading.
    /// </summary>
    public Windows.Storage.Streams.InMemoryRandomAccessStream? CoverStream => _state.CoverStream;

    public string SongName => NowPlayingProviderItem?.Name ?? NowPlayingItem?.Name ?? string.Empty;
    public string ArtistName => NowPlayingProviderItem?.CreatorList is { Count: > 0 } creators
        ? string.Join("; ", creators)
        : NowPlayingItem?.ArtistString ?? string.Empty;
    public string AlbumName => NowPlayingProviderItem?.Album?.Name ?? NowPlayingItem?.AlbumString ?? string.Empty;
    public string QualityTagText => NowPlayingProviderItem != null && !string.IsNullOrWhiteSpace(QualityTag)
        ? HyPlayItem.FormatAudioLevel(QualityTag)
        : NowPlayingItem?.GetQualityTagText(_setting.audioRate) ?? "无歌曲";
    public string TotalTimeText => FormatTime(Duration != TimeSpan.Zero ? Duration : TimeSpan.FromMilliseconds(NowPlayingProviderItem?.Duration ?? NowPlayingItem?.LengthInMilliseconds ?? 0));
    public string NowTimeText => FormatTime(Position);
    public double ProgressMilliseconds => Position.TotalMilliseconds;
    public double DurationMilliseconds => Duration != TimeSpan.Zero ? Duration.TotalMilliseconds : NowPlayingProviderItem?.Duration ?? NowPlayingItem?.LengthInMilliseconds ?? 0;
    public string PlayStateGlyph => IsPlaying ? "\uF8AE" : "\uF5B0";
    public bool CanShareCurrentSong => NowPlayingProviderItem is NeteaseSong
                                       || NowPlayingItem is { ItemType: not HyPlayItemType.Local and not HyPlayItemType.LocalProgressive };
    public DataTransferManager DataTransferManager => _dataTransferManager;

    // ── Relay Commands ──

    [RelayCommand]
    private void TogglePlayPause()
    {
        _control.TogglePlayPause();
    }

    [RelayCommand]
    private async Task MoveNextAsync()
    {
        await _playlist.MoveNextAsync(true);
    }

    [RelayCommand]
    private async Task MovePreviousAsync()
    {
        if (_state.IsInFm)
            PersonalFM.ExitFm();
        else
            await _playlist.MovePreviousAsync();
    }

    [RelayCommand]
    private void ChangePlayMode()
    {
        if (_state.IsInFm) return;

        var nextStrategy = ActiveStrategyId switch
        {
            "seq" => "shn",
            "shn" => "sgl",
            "sgl" => "seq",
            _ => "seq"
        };
        _playlist.SetStrategy(nextStrategy);
        ActiveStrategyId = nextStrategy;
    }

    [RelayCommand]
    private async Task SeekAsync(TimeSpan target)
    {
        await _control.SeekAsync(target);
    }

    [RelayCommand]
    private void LikeSong()
    {
        _authService.LikeSong();
    }

    [RelayCommand]
    private void RemoveAll()
    {
        _playlist.Clear();
    }

    [RelayCommand]
    private void SetVolume(double value)
    {
        _control.Volume = value / 100;
    }

    [RelayCommand]
    private void RemoveItem(HyPlayItem item)
    {
        if (item == null) return;
        var index = IndexOfPlaylistItem(item);
        if (index >= 0)
            _playlist.RemoveAt(index);
    }

    [RelayCommand]
    private async Task MoveToItemAsync(HyPlayItem item)
    {
        if (item == null || item == NowPlayingItem) return;
        var index = IndexOfPlaylistItem(item);
        if (index >= 0)
            await _playlist.MoveToIndexAsync(index);
    }

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
                    NowPlayingProviderItem = _state.NowPlayingProviderItem;
                    break;
                case nameof(PlaybackStateService.IsPlaying):
                    IsPlaying = _state.IsPlaying;
                    break;
                case nameof(PlaybackStateService.QualityTag):
                    QualityTag = _state.QualityTag;
                    break;
                case nameof(PlaybackStateService.LyricInfo):
                    LyricInfo = _state.LyricInfo;
                    break;
                case nameof(PlaybackStateService.Position):
                    Position = _state.Position;
                    Duration = _state.Duration;
                    break;
                case nameof(PlaybackStateService.LyricIndex):
                    LyricIndex = _state.LyricIndex;
                    break;
            }
        });
    }

    private void OnPlaylistChanged()
    {
        RunOnUIThread(() =>
        {
            ActiveStrategyId = _state.ActiveStrategyId;
            RefreshPlaylistItems();
        });
    }

    partial void OnNowPlayingItemChanged(HyPlayItem? value) => NotifyPlayBarProperties();
    partial void OnNowPlayingProviderItemChanged(SingleSongBase? value) => NotifyPlayBarProperties();
    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayStateGlyph));
    partial void OnPositionChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(NowTimeText));
        OnPropertyChanged(nameof(ProgressMilliseconds));
    }
    partial void OnDurationChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(TotalTimeText));
        OnPropertyChanged(nameof(DurationMilliseconds));
    }
    partial void OnActiveStrategyIdChanged(string value) => RefreshPlaylistItems();
    partial void OnQualityTagChanged(string value) => OnPropertyChanged(nameof(QualityTagText));

    private void NotifyPlayBarProperties()
    {
        OnPropertyChanged(nameof(SongName));
        OnPropertyChanged(nameof(ArtistName));
        OnPropertyChanged(nameof(AlbumName));
        OnPropertyChanged(nameof(QualityTagText));
        OnPropertyChanged(nameof(TotalTimeText));
        OnPropertyChanged(nameof(DurationMilliseconds));
        OnPropertyChanged(nameof(CanShareCurrentSong));
    }

    private void RunOnUIThread(Action action)
    {
        _taskRunner.Forget(_notification.InvokeOnUIThread(action), $"{nameof(PlayBarViewModel)} UI update");
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        if (time.Hours == 0)
            return time.Minutes < 10 ? time.ToString(@"m\:ss") : time.ToString(@"mm\:ss");
        return time.ToString(@"hh\:mm\:ss");
    }

    // ── Helpers ──

    /// <summary>
    /// Refreshes the PlaylistItems collection from the playlist service.
    /// </summary>
    public void RefreshPlaylistItems()
    {
        PlaylistItems.Clear();
        var snapshot = _playlist.LegacyItemsSnapshot;

        if (ActiveStrategyId == "shn" && _setting.displayShuffledList)
        {
            foreach (var idx in _playlist.ShuffleList)
            {
                if (idx >= 0 && idx < snapshot.Count)
                    PlaylistItems.Add(snapshot[idx]);
            }
        }
        else
        {
            foreach (var item in snapshot)
                PlaylistItems.Add(item);
        }
    }

    private int IndexOfPlaylistItem(HyPlayItem item)
    {
        var snapshot = _playlist.LegacyItemsSnapshot;
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (ReferenceEquals(snapshot[i], item))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Gets the current playlist title string.
    /// </summary>
    public string GetPlaylistTitle()
    {
        if (ActiveStrategyId == "shn" && _setting.displayShuffledList)
            return $"随机播放列表 (共{_playlist.QueueCount}首)";
        return $"播放列表 (共{_playlist.QueueCount}首)";
    }

    /// <summary>
    /// Gets the targeting index for the current play mode.
    /// </summary>
    public int GetTargetingIndex()
    {
        if (ActiveStrategyId == "shn" && _setting.displayShuffledList)
            return _playlist.ShufflingIndex;
        return _playlist.NowPlayingIndex;
    }

    public void SyncFromState()
    {
        NowPlayingItem = _state.NowPlayingItem;
        NowPlayingProviderItem = _state.NowPlayingProviderItem ?? _playlist.NowPlayingProviderItem;
        IsPlaying = _state.IsPlaying;
        Position = _state.Position;
        Duration = _state.Duration;
        Volume = _state.Volume;
        ActiveStrategyId = _state.ActiveStrategyId;
        LyricIndex = _state.LyricIndex;
        LyricInfo = _state.LyricInfo;
        IsInFm = _state.IsInFm;
        QualityTag = _state.QualityTag;
    }

    /// <summary>
    /// Notifies that playlist append is done (triggers PlaylistChanged message).
    /// </summary>
    public void NotifyAppendDone()
    {
        _playlist.NotifyAppendDone();
    }
}

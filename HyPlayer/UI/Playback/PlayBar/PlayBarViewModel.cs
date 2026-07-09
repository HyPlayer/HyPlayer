using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using CommunityToolkit.WinUI.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.UI.Playback.PlayBar;

public partial class PlayBarViewModel : ObservableObject
{
    private readonly PlayCoreBase _playCore;
    private readonly IPlaybackControlService _control;
    private readonly PlaybackStateService _state;
    private readonly ILyricService _lyricService;
    private readonly Setting _setting;
    private readonly INotificationService _notification;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly IAuthService _authService;
    private readonly WeakEventListener<PlayBarViewModel, object?, PropertyChangedEventArgs> _stateChangedListener;
    private int _queueCount;

    public PlayBarViewModel(
        PlayCoreBase playCore,
        IPlaybackControlService control,
        PlaybackStateService state,
        ILyricService lyricService,
        Setting setting,
        INotificationService notification,
        IBackgroundTaskRunner taskRunner,
        IAuthService authService)
    {
        _playCore = playCore;
        _control = control;
        _state = state;
        _lyricService = lyricService;
        _setting = setting;
        _notification = notification;
        _taskRunner = taskRunner;
        _authService = authService;
        SyncFromState();
        _stateChangedListener = new WeakEventListener<PlayBarViewModel, object?, PropertyChangedEventArgs>(this)
        {
            OnEventAction = static (instance, _, args) => instance.OnPlaybackStatePropertyChanged(args.PropertyName),
            OnDetachAction = weakEventListener => { _state.PropertyChanged -= weakEventListener.OnEvent; }
        };
        _state.PropertyChanged += _stateChangedListener.OnEvent;
    }

    // ── Observable Properties (partial property pattern for AOT) ──

    [ObservableProperty]
    public partial SingleSongBase? NowPlayingProviderItem { get; set; }

    [ObservableProperty]
    public partial PlaybackCurrentItemSnapshot? NowPlayingSnapshot { get; set; }

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
    public ObservableCollection<PlayBarQueueItem> PlaylistItems { get; } = [];

    [ObservableProperty]
    public partial PlayBarQueueItem? CurrentPlaylistItem { get; set; }

    // ── Playlist service pass-through ──

    public int QueueCount => _queueCount;
    public int NowPlayingIndex => _state.NowPlayingIndex;
    public string PlaySourceId => _playCore.PlaySourceId;

    /// <summary>
    /// Pass-through to PlaybackStateService.CoverStream for UI cover loading.
    /// </summary>
    public Windows.Storage.Streams.InMemoryRandomAccessStream? CoverStream => _state.CoverStream;

    public string SongName => NowPlayingSnapshot?.Name ?? string.Empty;
    public string ArtistName => NowPlayingSnapshot?.ArtistText ?? string.Empty;
    public string AlbumName => NowPlayingSnapshot?.AlbumName ?? string.Empty;
    public string QualityTagText => GetQualityTagText(NowPlayingSnapshot, QualityTag, _setting.audioRate);
    public string TotalTimeText => FormatTime(Duration != TimeSpan.Zero ? Duration : TimeSpan.FromMilliseconds(NowPlayingSnapshot?.Duration ?? 0));
    public string NowTimeText => FormatTime(Position);
    public double ProgressMilliseconds => Position.TotalMilliseconds;
    public double DurationMilliseconds => Duration != TimeSpan.Zero ? Duration.TotalMilliseconds : NowPlayingSnapshot?.Duration ?? 0;
    public string PlayStateGlyph => IsPlaying ? "\uF8AE" : "\uF5B0";
    public bool CanShareCurrentSong => NowPlayingSnapshot is { IsLocal: false };

    // ── Relay Commands ──

    [RelayCommand]
    private void TogglePlayPause()
    {
        _control.TogglePlayPause();
    }

    [RelayCommand]
    private async Task MoveNextAsync()
    {
        await _control.MoveNextAndPlayAsync(true);
    }

    [RelayCommand]
    private async Task MovePreviousAsync()
    {
        if (_state.IsInFm)
            PersonalFM.ExitFm();
        else
            await _control.MovePreviousAndPlayAsync();
    }

    [RelayCommand]
    private async Task ChangePlayModeAsync()
    {
        if (_state.IsInFm) return;

        var nextStrategy = ActiveStrategyId switch
        {
            "seq" => "shn",
            "shn" => "sgl",
            "sgl" => "seq",
            _ => "seq"
        };
        await _playCore.SetPlayModeAsync(nextStrategy);
        _state.ActiveStrategyId = nextStrategy;
        _setting.ActiveStrategyId = nextStrategy;
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
        _taskRunner.Forget(_control.StopAsync(), "stop before clearing PlayCore queue");
        _taskRunner.Forget(_playCore.RemoveAllSongAsync(), "clear PlayCore queue");
        _state.ClearNowPlaying();
    }

    [RelayCommand]
    private void SetVolume(double value)
    {
        _control.Volume = value / 100;
    }

    [RelayCommand]
    private void RemoveItem(PlayBarQueueItem item)
    {
        if (item == null) return;
        var queue = PlayCoreQueueSnapshot.GetPlaylist(_playCore);
        if (item.QueueIndex >= 0 && item.QueueIndex < queue.Count)
            _taskRunner.Forget(_playCore.RemoveSongAsync(queue[item.QueueIndex]), "remove PlayCore queue item");
    }

    [RelayCommand]
    private async Task MoveToItemAsync(PlayBarQueueItem item)
    {
        if (item == null || item.QueueIndex == NowPlayingIndex) return;
        var queue = PlayCoreQueueSnapshot.GetPlaylist(_playCore);
        if (item.QueueIndex >= 0 && item.QueueIndex < queue.Count)
        {
            await _playCore.MovePointerToIndexAsync(item.QueueIndex);
            if (_playCore.CurrentSong is { } song)
                await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
        }
    }

    private void OnPlaybackStatePropertyChanged(string? propertyName)
    {
        RunOnUIThread(() =>
        {
            switch (propertyName)
            {
                case nameof(PlaybackStateService.NowPlayingSnapshot):
                    SyncFromState();
                    break;
                case nameof(PlaybackStateService.NowPlayingProviderItem):
                    NowPlayingProviderItem = _state.NowPlayingProviderItem;
                    break;
                case nameof(PlaybackStateService.NowPlayingIndex):
                    OnPropertyChanged(nameof(NowPlayingIndex));
                    UpdateCurrentPlaylistItem();
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
                case nameof(PlaybackStateService.QueueRevision):
                    OnPlaylistChanged();
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

    partial void OnNowPlayingProviderItemChanged(SingleSongBase? value)
    {
        NotifyPlayBarProperties();
        UpdateCurrentPlaylistItem();
    }

    partial void OnNowPlayingSnapshotChanged(PlaybackCurrentItemSnapshot? value)
    {
        NotifyPlayBarProperties();
        UpdateCurrentPlaylistItem();
    }
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

    private static string GetQualityTagText(PlaybackCurrentItemSnapshot? snapshot, string qualityTag, string fallbackLevel)
    {
        if (!string.IsNullOrWhiteSpace(qualityTag)) return FormatAudioLevel(qualityTag);
        if (snapshot is null) return "无歌曲";
        if (snapshot.IsLocal) return "本地歌曲";
        return FormatAudioLevel(fallbackLevel);
    }

    private static string FormatAudioLevel(string level)
    {
        return level switch
        {
            "standard" => "标准",
            "higher" => "较高",
            "exhigh" => "极高",
            "lossless" => "无损",
            "hires" => "Hi-Res",
            "jyeffect" => "高清环绕声",
            "sky" => "沉浸环绕声",
            "jymaster" => "超清母带",
            _ => string.Empty
        };
    }

    // ── Helpers ──

    /// <summary>
    /// Refreshes the PlaylistItems collection from the playlist service.
    /// </summary>
    public void RefreshPlaylistItems()
    {
        PlaylistItems.Clear();
        CurrentPlaylistItem = null;
        var queueSnapshot = PlayCoreQueueSnapshot.GetQueueItems(_playCore);
        var orderedQueue = PlayCoreQueueSnapshot.GetOrderedPlaylist(_playCore);
        var queue = PlayCoreQueueSnapshot.GetPlaylist(_playCore);
        _queueCount = queueSnapshot.Count;
        OnPropertyChanged(nameof(QueueCount));

        if (ActiveStrategyId == "shn" && _setting.displayShuffledList)
        {
            foreach (var orderedSong in orderedQueue)
            {
                var idx = IndexOfQueueItem(queue, orderedSong);
                AddPlaylistRow(idx, queueSnapshot);
            }
        }
        else
        {
            for (var idx = 0; idx < queueSnapshot.Count; idx++)
            {
                AddPlaylistRow(idx, queueSnapshot);
            }
        }
    }

    private void UpdateCurrentPlaylistItem()
    {
        var currentIndex = NowPlayingIndex;
        PlayBarQueueItem? currentItem = null;
        foreach (var item in PlaylistItems)
        {
            var isCurrent = item.QueueIndex == currentIndex;
            if (item.IsCurrent != isCurrent)
                item.IsCurrent = isCurrent;
            if (isCurrent)
                currentItem = item;
        }

        CurrentPlaylistItem = currentItem;
    }

    private void AddPlaylistRow(int queueIndex, IReadOnlyList<PlaybackQueueItemSnapshot> queueSnapshot)
    {
        if (queueIndex < 0 || queueIndex >= queueSnapshot.Count)
            return;

        var row = PlayBarQueueItem.FromSnapshot(queueSnapshot[queueIndex], NowPlayingIndex);
        PlaylistItems.Add(row);
        if (row.IsCurrent)
            CurrentPlaylistItem = row;
    }

    private static int IndexOfQueueItem(IReadOnlyList<SingleSongBase> queue, SingleSongBase item)
    {
        for (var i = 0; i < queue.Count; i++)
        {
            if (Equals(queue[i], item))
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
            return $"随机播放列表 (共{QueueCount}首)";
        return $"播放列表 (共{QueueCount}首)";
    }

    /// <summary>
    /// Gets the targeting index for the current play mode.
    /// </summary>
    public int GetTargetingIndex()
    {
        if (ActiveStrategyId == "shn" && _setting.displayShuffledList)
            return _state.NowPlayingProviderItem is { } providerItem
                ? IndexOfQueueItem(PlayCoreQueueSnapshot.GetOrderedPlaylist(_playCore), providerItem)
                : -1;
        return _state.NowPlayingIndex;
    }

    public void SyncFromState()
    {
        NowPlayingProviderItem = _state.NowPlayingProviderItem ?? _playCore.CurrentSong;
        NowPlayingSnapshot = _state.NowPlayingSnapshot ?? PlaybackCurrentItemSnapshot.FromProvider(NowPlayingProviderItem);
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
}
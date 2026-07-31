using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Application.Threading;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.UI.Playback.PlayBar;

public partial class PlayBarViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IPlaybackControlService _control;
    private readonly ILyricService _lyricService;
    private readonly PlayCoreBase _playCore;
    private readonly PlaybackSettings _playbackSettings;
    private readonly UISettings _uiSettings;
    private readonly PlaybackStateService _state;
    private readonly WeakEventListener<PlayBarViewModel, object?, PropertyChangedEventArgs> _stateChangedListener;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly IUIThreadDispatcher _uiThreadDispatcher;

    public PlayBarViewModel(
        PlayCoreBase playCore,
        IPlaybackControlService control,
        PlaybackStateService state,
        ILyricService lyricService,
        PlaybackSettings playbackSettings,
        UISettings uiSettings,
        IUIThreadDispatcher uiThreadDispatcher,
        IBackgroundTaskRunner taskRunner,
        IAuthService authService)
    {
        _playCore = playCore;
        _control = control;
        _state = state;
        _lyricService = lyricService;
        _playbackSettings = playbackSettings;
        _uiSettings = uiSettings;
        _uiThreadDispatcher = uiThreadDispatcher;
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

    [ObservableProperty] public partial SingleSongBase? NowPlayingProviderItem { get; set; }

    [ObservableProperty] public partial PlaybackCurrentItemSnapshot? NowPlayingSnapshot { get; set; }

    [ObservableProperty] public partial bool IsPlaying { get; set; }

    [ObservableProperty] public partial TimeSpan Position { get; set; }

    [ObservableProperty] public partial TimeSpan Duration { get; set; }

    [ObservableProperty] public partial double Volume { get; set; }

    [ObservableProperty] public partial string ActiveStrategyId { get; set; } = "seq";

    [ObservableProperty] public partial int LyricIndex { get; set; }

    [ObservableProperty] public partial HyLyricInfo LyricInfo { get; set; } = new();

    [ObservableProperty] public partial bool IsInFm { get; set; }

    [ObservableProperty] public partial string QualityTag { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackAccentBrush))]
    [NotifyPropertyChangedFor(nameof(PlaybackAccentTheme))]
    public partial PlaybackThemeSnapshot DisplayedTheme { get; set; } = PlaybackThemeSnapshot.Default;

    public SolidColorBrush PlaybackAccentBrush => DisplayedTheme.AccentBrush;
    public ElementTheme PlaybackAccentTheme => DisplayedTheme.IsBright ? ElementTheme.Light : ElementTheme.Dark;

    /// <summary>
    ///     Observable playlist items for the ListBox binding.
    /// </summary>
    public ObservableCollection<PlayBarQueueItem> PlaylistItems { get; } = [];

    [ObservableProperty] public partial PlayBarQueueItem? CurrentPlaylistItem { get; set; }

    // ── Playlist service pass-through ──

    public int QueueCount { get; private set; }

    public int NowPlayingIndex => _state.NowPlayingIndex;
    public string PlaySourceId => _playCore.PlaySourceId;

    /// <summary>
    ///     Pass-through to PlaybackStateService.CoverStream for UI cover loading.
    /// </summary>
    public InMemoryRandomAccessStream? CoverStream => _state.CoverStream;

    public string SongName => NowPlayingSnapshot?.Name ?? string.Empty;
    public string ArtistName => NowPlayingSnapshot?.ArtistText ?? string.Empty;
    public string AlbumName => NowPlayingSnapshot?.AlbumName ?? string.Empty;
    public string QualityTagText => GetQualityTagText(NowPlayingSnapshot, QualityTag, _playbackSettings.AudioRate);

    public string TotalTimeText => FormatTime(Duration != TimeSpan.Zero
        ? Duration
        : TimeSpan.FromMilliseconds(NowPlayingSnapshot?.Duration ?? 0));

    public string NowTimeText => FormatTime(Position);
    public double ProgressMilliseconds => Position.TotalMilliseconds;

    public double DurationMilliseconds =>
        Duration != TimeSpan.Zero ? Duration.TotalMilliseconds : NowPlayingSnapshot?.Duration ?? 0;

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
        await _control.SetPlayModeAsync(nextStrategy);
        _playbackSettings.ActiveStrategyId = nextStrategy;
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
        _taskRunner.Forget(StopAndClearQueueAsync(), "stop and clear PlayCore queue");
        _state.ClearNowPlaying();
    }

    private async Task StopAndClearQueueAsync()
    {
        await _control.StopAsync().ConfigureAwait(false);
        await _control.ClearQueueAsync().ConfigureAwait(false);
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
        if (item?.ProviderItem is not { } selectedSong
            || SameSong(selectedSong, NowPlayingProviderItem))
            return;

        await _playCore.MovePointerToAsync(selectedSong);
        if (_playCore.CurrentSong is { } currentSong && SameSong(currentSong, selectedSong))
            await _control.LoadAndPlayAsync(currentSong, removeCurrentSongs: false);
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

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayStateGlyph));
    }

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

    partial void OnActiveStrategyIdChanged(string value)
    {
        RefreshPlaylistItems();
    }

    partial void OnQualityTagChanged(string value)
    {
        OnPropertyChanged(nameof(QualityTagText));
    }

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
        _taskRunner.Forget(_uiThreadDispatcher.TryRunAsync(action), $"{nameof(PlayBarViewModel)} UI update");
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        if (time.Hours == 0)
            return time.Minutes < 10 ? time.ToString(@"m\:ss") : time.ToString(@"mm\:ss");
        return time.ToString(@"hh\:mm\:ss");
    }

    private static string GetQualityTagText(PlaybackCurrentItemSnapshot? snapshot, string qualityTag,
        string fallbackLevel)
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
    ///     Refreshes the PlaylistItems collection from the playlist service.
    /// </summary>
    public void RefreshPlaylistItems()
    {
        PlaylistItems.Clear();
        CurrentPlaylistItem = null;
        var queueSnapshot = PlayCoreQueueSnapshot.GetQueueItems(_playCore);
        var orderedQueue = PlayCoreQueueSnapshot.GetOrderedPlaylist(_playCore);
        var queue = PlayCoreQueueSnapshot.GetPlaylist(_playCore);
        QueueCount = queueSnapshot.Count;
        OnPropertyChanged(nameof(QueueCount));

        if (ActiveStrategyId == "shn" && _uiSettings.DisplayShuffledList)
            foreach (var orderedSong in orderedQueue)
            {
                var idx = IndexOfQueueItem(queue, orderedSong);
                AddPlaylistRow(idx, queueSnapshot);
            }
        else
            for (var idx = 0; idx < queueSnapshot.Count; idx++)
                AddPlaylistRow(idx, queueSnapshot);

        UpdateCurrentPlaylistItem();
    }

    private void UpdateCurrentPlaylistItem()
    {
        PlayBarQueueItem? currentItem = null;
        foreach (var item in PlaylistItems)
        {
            var isCurrent = SameSong(item.ProviderItem, NowPlayingProviderItem);
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

        var row = PlayBarQueueItem.FromSnapshot(queueSnapshot[queueIndex], NowPlayingProviderItem);
        PlaylistItems.Add(row);
        if (row.IsCurrent)
            CurrentPlaylistItem = row;
    }

    private static int IndexOfQueueItem(IReadOnlyList<SingleSongBase> queue, SingleSongBase item)
    {
        for (var i = 0; i < queue.Count; i++)
            if (SameSong(queue[i], item))
                return i;

        return -1;
    }

    /// <summary>
    ///     Gets the current playlist title string.
    /// </summary>
    public string GetPlaylistTitle()
    {
        if (ActiveStrategyId == "shn" && _uiSettings.DisplayShuffledList)
            return $"随机播放列表 (共{QueueCount}首)";
        return $"播放列表 (共{QueueCount}首)";
    }

    /// <summary>
    ///     Gets the targeting index for the current play mode.
    /// </summary>
    public int GetTargetingIndex()
    {
        for (var index = 0; index < PlaylistItems.Count; index++)
            if (SameSong(PlaylistItems[index].ProviderItem, NowPlayingProviderItem))
                return index;

        return -1;
    }

    private static bool SameSong(SingleSongBase? left, SingleSongBase? right)
    {
        return ReferenceEquals(left, right)
               || (left is not null
                   && right is not null
                   && left.ProviderId == right.ProviderId
                   && left.TypeId == right.TypeId
                   && left.ActualId == right.ActualId);
    }

    public void SyncFromState()
    {
        NowPlayingProviderItem = _state.NowPlayingProviderItem ?? _playCore.CurrentSong;
        NowPlayingSnapshot = _state.NowPlayingSnapshot ??
                             PlaybackCurrentItemSnapshot.FromProvider(NowPlayingProviderItem);
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

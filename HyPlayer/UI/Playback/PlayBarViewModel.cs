using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using System.Diagnostics.CodeAnalysis;
using Windows.ApplicationModel.DataTransfer;
namespace HyPlayer.ViewModels;

public partial class PlayBarViewModel : ObservableRecipient
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

        // Activate messenger registrations
        IsActive = true;
    }

    // ── Observable Properties (partial property pattern for AOT) ──

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

    public IReadOnlyList<HyPlayItem> Items => _playlist.Items;
    public int NowPlayingIndex => _playlist.NowPlayingIndex;
    public string PlaySourceId => _playlist.PlaySourceId;

    /// <summary>
    /// Pass-through to PlaybackStateService.CoverStream for UI cover loading.
    /// </summary>
    public Windows.Storage.Streams.InMemoryRandomAccessStream? CoverStream => _state.CoverStream;

    public string SongName => NowPlayingItem?.Name ?? string.Empty;
    public string ArtistName => NowPlayingItem?.ArtistString ?? string.Empty;
    public string AlbumName => NowPlayingItem?.AlbumString ?? string.Empty;
    public string QualityTagText => NowPlayingItem?.GetQualityTagText(_setting.audioRate) ?? "无歌曲";
    public string TotalTimeText => FormatTime(Duration != TimeSpan.Zero ? Duration : TimeSpan.FromMilliseconds(NowPlayingItem?.LengthInMilliseconds ?? 0));
    public string NowTimeText => FormatTime(Position);
    public double ProgressMilliseconds => Position.TotalMilliseconds;
    public double DurationMilliseconds => Duration != TimeSpan.Zero ? Duration.TotalMilliseconds : NowPlayingItem?.LengthInMilliseconds ?? 0;
    public string PlayStateGlyph => IsPlaying ? "\uF8AE" : "\uF5B0";
    public bool CanShareCurrentSong => NowPlayingItem is { ItemType: not HyPlayItemType.Local and not HyPlayItemType.LocalProgressive };
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
        var index = _playlist.Items.ToList().IndexOf(item);
        if (index >= 0)
            _playlist.RemoveAt(index);
    }

    [RelayCommand]
    private async Task MoveToItemAsync(HyPlayItem item)
    {
        if (item != null && item != _playlist.NowPlayingItem)
            await _playlist.MoveToAsync(item);
    }

    // ── Messenger Registrations ──
    [RequiresUnreferencedCode("This method requires the generated CommunityToolkit.Mvvm.Messaging.__Internals.__IMessengerExtensions type not to be removed to use the fast path. If this type is removed by the linker, or if the target recipient was created dynamically and was missed by the source generator, a slower fallback path using a compiled LINQ expression will be used. This will have more overhead in the first invocation of this method for any given recipient type. Alternatively, OnActivated() can be manually overwritten, and registration can be done individually for each required message for this recipient.")]
    [RequiresDynamicCode("This method requires the generated CommunityToolkit.Mvvm.Messaging.__Internals.__IMessengerExtensions type not to be removed to use the fast path. If that is present, the method is AOT safe, as the only methods being invoked to register the messages will be the ones produced by the source generator. If it isn't, this method will need to dynamically create the generic methods to register messages, which might not be available at runtime. Alternatively, OnActivated() can be manually overwritten, and registration can be done individually for each required message for this recipient.")]
    protected override void OnActivated()
    {
        var messenger = Messenger;

        messenger.Register<TrackChangedMessage>(this, (r, _) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.RunOnUIThread(vm.SyncFromState);
        });

        messenger.Register<PlaybackStateChangedMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.RunOnUIThread(() => vm.IsPlaying = m.IsPlaying);
        });

        messenger.Register<QualityTagChangedMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.RunOnUIThread(() => vm.QualityTag = m.Tag);
        });

        messenger.Register<LyricLoadedMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.RunOnUIThread(() => vm.LyricInfo = m.Info);
        });

        // ── High-frequency position: keep direct subscription ──
        messenger.Register<PositionTickMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.RunOnUIThread(() =>
            {
                vm.Position = m.Position;
                vm.Duration = vm._state.Duration;
            });
        });

        messenger.Register<LyricIndexChangedMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.RunOnUIThread(() => vm.LyricIndex = m.Index);
        });

        // ── Playlist: keep for playlist-refresh and strategy-sync behavior ──
        messenger.Register<PlaylistChangedMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.RunOnUIThread(() =>
            {
                vm.ActiveStrategyId = vm._state.ActiveStrategyId;
                vm.RefreshPlaylistItems();
            });
        });
    }

    partial void OnNowPlayingItemChanged(HyPlayItem? value) => NotifyPlayBarProperties();
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
        var snapshot = _playlist.Items;

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

    /// <summary>
    /// Gets the current playlist title string.
    /// </summary>
    public string GetPlaylistTitle()
    {
        if (ActiveStrategyId == "shn" && _setting.displayShuffledList)
            return $"随机播放列表 (共{_playlist.Items.Count}首)";
        return $"播放列表 (共{_playlist.Items.Count}首)";
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

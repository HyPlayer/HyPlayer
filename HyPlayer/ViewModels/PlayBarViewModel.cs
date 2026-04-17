using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.ViewModels;

public partial class PlayBarViewModel : ObservableRecipient
{
    private readonly IPlaylistService _playlist;
    private readonly IPlaybackControlService _control;
    private readonly PlaybackStateService _state;
    private readonly ILyricService _lyricService;
    private readonly Setting _setting;

    public PlayBarViewModel(
        IPlaylistService playlist,
        IPlaybackControlService control,
        PlaybackStateService state,
        ILyricService lyricService,
        Setting setting)
    {
        _playlist = playlist;
        _control = control;
        _state = state;
        _lyricService = lyricService;
        _setting = setting;

        _state.PropertyChanged += State_PropertyChanged;

        // Initialize from current state
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

    /// <summary>
    /// Current play mode derived from ActiveStrategyId.
    /// </summary>
    public PlayMode NowPlayType => ActiveStrategyId switch
    {
        "sgl" => PlayMode.SinglePlay,
        "shf" or "shn" => PlayMode.Shuffled,
        _ => PlayMode.DefaultRoll
    };

    // ── Playlist service pass-through ──

    public IReadOnlyList<HyPlayItem> Items => _playlist.Items;
    public int NowPlayingIndex => _playlist.NowPlayingIndex;
    public string PlaySourceId => _playlist.PlaySourceId;

    /// <summary>
    /// Pass-through to PlaybackStateService.CoverStream for UI cover loading.
    /// </summary>
    public Windows.Storage.Streams.InMemoryRandomAccessStream? CoverStream => _state.CoverStream;

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
        if (Common.IsInFm)
            PersonalFM.ExitFm();
        else
            await _playlist.MovePreviousAsync();
    }

    [RelayCommand]
    private void ChangePlayMode()
    {
        if (Common.IsInFm) return;

        var nextStrategy = ActiveStrategyId switch
        {
            "seq" => "shn",
            "shn" or "shf" => "sgl",
            "sgl" => "seq",
            _ => "seq"
        };
        _playlist.SetStrategy(nextStrategy);
        ActiveStrategyId = nextStrategy;
        OnPropertyChanged(nameof(NowPlayType));
    }

    [RelayCommand]
    private async Task SeekAsync(TimeSpan target)
    {
        await _control.SeekAsync(target);
    }

    [RelayCommand]
    private void LikeSong()
    {
        HyPlayList.LikeSong();
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

    protected override void OnActivated()
    {
        var messenger = Messenger;

        messenger.Register<TrackChangedMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.NowPlayingItem = m.Item;
        });

        messenger.Register<PlaybackStateChangedMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.IsPlaying = m.IsPlaying;
        });

        messenger.Register<PlaylistChangedMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.RefreshPlaylistItems(m.IsShuffleTrigger);
        });

        messenger.Register<CoverChangedMessage>(this, (r, _) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.OnPropertyChanged(nameof(NowPlayingItem));
        });

        messenger.Register<PositionTickMessage>(this, (r, m) =>
        {
            var vm = (PlayBarViewModel)r;
            vm.Position = m.Position;
        });

        messenger.Register<SongLikeStatusChangedMessage>(this, (r, m) =>
        {
            // UI layer handles visual update; ViewModel just notifies
            var vm = (PlayBarViewModel)r;
            vm.OnPropertyChanged(nameof(NowPlayingItem));
        });

        messenger.Register<LoginCompletedMessage>(this, (r, _) =>
        {
            // UI layer handles login-done logic
        });
    }

    // ── State forwarding ──

    private void State_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlaybackStateService.NowPlayingItem):
                NowPlayingItem = _state.NowPlayingItem;
                break;
            case nameof(PlaybackStateService.IsPlaying):
                IsPlaying = _state.IsPlaying;
                break;
            case nameof(PlaybackStateService.Position):
                Position = _state.Position;
                break;
            case nameof(PlaybackStateService.Duration):
                Duration = _state.Duration;
                break;
            case nameof(PlaybackStateService.Volume):
                Volume = _state.Volume;
                break;
            case nameof(PlaybackStateService.ActiveStrategyId):
                ActiveStrategyId = _state.ActiveStrategyId;
                OnPropertyChanged(nameof(NowPlayType));
                break;
            case nameof(PlaybackStateService.LyricIndex):
                LyricIndex = _state.LyricIndex;
                break;
            case nameof(PlaybackStateService.LyricInfo):
                LyricInfo = _state.LyricInfo;
                break;
            case nameof(PlaybackStateService.IsInFm):
                IsInFm = _state.IsInFm;
                break;
            case nameof(PlaybackStateService.QualityTag):
                QualityTag = _state.QualityTag;
                break;
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Refreshes the PlaylistItems collection from the playlist service.
    /// </summary>
    public void RefreshPlaylistItems(bool isShuffleTrigger = false)
    {
        PlaylistItems.Clear();

        if (NowPlayType == PlayMode.Shuffled && _setting.shuffleNoRepeating && _setting.displayShuffledList)
        {
            foreach (var idx in HyPlayList.ShuffleList)
            {
                if (idx >= 0 && idx < _playlist.Items.Count)
                    PlaylistItems.Add(_playlist.Items[idx]);
            }
        }
        else
        {
            foreach (var item in _playlist.Items)
                PlaylistItems.Add(item);
        }
    }

    /// <summary>
    /// Gets the current playlist title string.
    /// </summary>
    public string GetPlaylistTitle()
    {
        if (NowPlayType == PlayMode.Shuffled && _setting.shuffleNoRepeating && _setting.displayShuffledList)
            return $"随机播放列表 (共{PlaylistItems.Count}首)";
        return $"播放列表 (共{PlaylistItems.Count}首)";
    }

    /// <summary>
    /// Gets the targeting index for the current play mode.
    /// </summary>
    public int GetTargetingIndex()
    {
        if (NowPlayType == PlayMode.Shuffled && _setting.shuffleNoRepeating && _setting.displayShuffledList)
            return HyPlayList.ShufflingIndex;
        return _playlist.NowPlayingIndex;
    }

    /// <summary>
    /// Notifies that playlist append is done (triggers PlaylistChanged message).
    /// </summary>
    public void NotifyAppendDone(bool isShuffleTrigger = false)
    {
        _playlist.NotifyAppendDone(isShuffleTrigger);
    }
}

#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

#endregion

namespace HyPlayer.Infrastructure.Netease;

/// <summary>
/// 私人 FM 控制器。
/// <para>
/// 保留静态入口 <see cref="InitPersonalFM"/> / <see cref="ExitFm"/> 以兼容现有调用方，
/// 具体的 FM 内容加载与曲目转换由 <see cref="IAsyncPlayStrategy"/> 的 pfm 策略负责。
/// </para>
/// </summary>
internal sealed class PersonalFM
{
    private static PersonalFM? _instance;

    private readonly IPlaylistService _playlistService;
    private readonly PlaybackStateService _playbackState;
    private readonly IAsyncPlayStrategy _personalFmStrategy;
    private string _previousStrategyId = string.Empty;
    private bool _isLoadingNextTrack;

    private PersonalFM()
    {
        _playlistService = Ioc.Default.GetRequiredService<IPlaylistService>();
        _playbackState = Ioc.Default.GetRequiredService<PlaybackStateService>();
        _personalFmStrategy = Ioc.Default.GetRequiredService<IEnumerable<IPlayStrategy>>()
            .OfType<IAsyncPlayStrategy>()
            .First(strategy => strategy.Id == "pfm");
    }

    public static void InitPersonalFM()
    {
        DisposeInstance();

        var fm = new PersonalFM();
        _instance = fm;
        fm._previousStrategyId = fm._playbackState.ActiveStrategyId;
        fm._playlistService.Clear();
        fm._playlistService.SetStrategy("pfm", persist: false);
        fm._playbackState.IsInFm = true;
        fm.LoadNextTrackAsync().SafeFireAndForget();
    }

    public static void ExitFm(bool clearPlaylist = true)
    {
        DisposeInstance(clearPlaylist);
    }

    /// <summary>
    /// 静态入口：切换到下一首 FM 歌曲（供旧代码调用）。
    /// </summary>
    public static void LoadNextFMStatic()
    {
        _instance?.LoadNextTrackAsync().SafeFireAndForget();
    }

    private async Task LoadNextTrackAsync(bool userInitiated = false)
    {
        if (_isLoadingNextTrack)
            return;

        _isLoadingNextTrack = true;
        try
        {
            if (_playlistService.NowPlayingIndex + 1 >= _playlistService.QueueCount)
                await AppendMoreTracksAsync();

            if (!IsActiveSession || _playlistService.QueueCount == 0)
                return;

            await _playlistService.MoveNextAsync(userInitiated);
            if (IsActiveSession)
                _playbackState.IsInFm = true;
        }
        finally
        {
            _isLoadingNextTrack = false;
        }
    }

    private async Task AppendMoreTracksAsync()
    {
        var moreItems = (await _personalFmStrategy.LoadMoreProviderItemsAsync(new PlayStrategyContext
        {
            CurrentIndex = _playlistService.NowPlayingIndex,
            QueueCount = _playlistService.QueueCount,
            ProviderItems = _playlistService.ProviderItems.ToArray(),
            ProviderQueueItems = _playlistService.ProviderQueueSnapshot.ToArray(),
            CurrentProviderItem = _playlistService.NowPlayingProviderItem
        })).ToList();

        if (!IsActiveSession || moreItems.Count == 0)
            return;

        _playlistService.AppendItems(moreItems);

    }

    private bool IsActiveSession => ReferenceEquals(_instance, this) && _playbackState.IsInFm;

    private static void DisposeInstance(bool clearPlaylist = true)
    {
        if (_instance is null)
            return;

        _instance._playbackState.IsInFm = false;
        if (clearPlaylist)
            _instance._playlistService.Clear();
        _instance._playlistService.SetStrategy(_instance._previousStrategyId);
        _instance = null;
    }
}

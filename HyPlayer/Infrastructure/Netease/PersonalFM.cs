#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
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
/// 保留静态入口 <see cref="InitPersonalFM"/> / <see cref="ExitFm"/> 以兼容现有调用方。
/// 播放队列和指针直接由 PlayCore 管理。
/// </para>
/// </summary>
internal sealed class PersonalFM
{
    private static PersonalFM? _instance;

    private readonly PlayCoreBase _playCore;
    private readonly IPlaybackControlService _control;
    private readonly PlaybackStateService _playbackState;
    private readonly Setting _setting;
    private string _previousStrategyId = string.Empty;
    private bool _isLoadingNextTrack;

    private PersonalFM()
    {
        _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
        _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
        _playbackState = Ioc.Default.GetRequiredService<PlaybackStateService>();
        _setting = Ioc.Default.GetRequiredService<Setting>();
    }

    public static void InitPersonalFM()
    {
        DisposeInstance();

        var fm = new PersonalFM();
        _instance = fm;
        fm._previousStrategyId = fm._playbackState.ActiveStrategyId;
        fm._playCore.RemoveAllSongAsync().SafeFireAndForget();
        fm._playCore.SetPlayModeAsync("pfm").SafeFireAndForget();
        fm._playbackState.ActiveStrategyId = "pfm";
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

    public static async Task AppendMoreTracksAsync()
    {
        if (_instance is { } instance)
            await instance.AppendMoreTracksCoreAsync().ConfigureAwait(false);
    }

    private async Task LoadNextTrackAsync(bool userInitiated = false)
    {
        if (_isLoadingNextTrack)
            return;

        _isLoadingNextTrack = true;
        try
        {
            var queue = await _playCore.GetPlaylistAsync().ConfigureAwait(false);
            var queueCount = queue.Count;
            if (await _playCore.GetCurrentIndexAsync().ConfigureAwait(false) + 1 >= queueCount)
            {
                await AppendMoreTracksCoreAsync().ConfigureAwait(false);
                queue = await _playCore.GetPlaylistAsync().ConfigureAwait(false);
            }

            if (!IsActiveSession || queue.Count == 0)
                return;

            await _control.MoveNextAndPlayAsync(userInitiated).ConfigureAwait(false);
            if (IsActiveSession)
                _playbackState.IsInFm = true;
        }
        finally
        {
            _isLoadingNextTrack = false;
        }
    }

    private async Task AppendMoreTracksCoreAsync()
    {
        var currentSong = _playbackState.NowPlayingProviderItem;
        var songs = _setting.useAiDj && currentSong is not null
            ? await LoadAiDjAsync(currentSong).ConfigureAwait(false)
            : await LoadPersonalFmAsync().ConfigureAwait(false);

        if (songs.Count > 0)
            await _playCore.InsertSongRangeAsync(songs).ConfigureAwait(false);
    }

    private bool IsActiveSession => ReferenceEquals(_instance, this) && _playbackState.IsInFm;

    private static void DisposeInstance(bool clearPlaylist = true)
    {
        if (_instance is null)
            return;

        _instance._playbackState.IsInFm = false;
        if (clearPlaylist)
            _instance._playCore.RemoveAllSongAsync().SafeFireAndForget();

        _instance._playCore.SetPlayModeAsync(_instance._previousStrategyId).SafeFireAndForget();
        _instance._playbackState.ActiveStrategyId = _instance._previousStrategyId;
        _instance = null;
    }

    private static async Task<List<SingleSongBase>> LoadPersonalFmAsync()
    {
        return (await new NeteasePersonalFMContainer { ActualId = "default", Name = "私人 FM" }
                .GetNextItemsRangeAsync()
                .ConfigureAwait(false))
            .OfType<SingleSongBase>()
            .ToList();
    }

    private static async Task<List<SingleSongBase>> LoadAiDjAsync(SingleSongBase currentSong)
    {
        var itemId = currentSong.ActualId ?? currentSong.Name;
        var container = new NeteaseContextRecommendationContainer
        {
            ActualId = itemId,
            SeedItemId = itemId,
            Name = "相关推荐",
            Count = 10
        };

        var songs = (await container.GetAllItemsAsync().ConfigureAwait(false))
            .OfType<SingleSongBase>()
            .ToList();

        return songs.Count > 0 ? songs : await LoadPersonalFmAsync().ConfigureAwait(false);
    }
}

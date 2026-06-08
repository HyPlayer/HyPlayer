using HyPlayer.Infrastructure.Netease;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    public async Task MoveNextAsync(bool userInitiated = false)
    {
        if (QueueCount == 0)
            return;

        if (_activeStrategyId == "pfm" && NowPlayingIndex + 1 >= QueueCount)
            await AppendMorePersonalFmTracksAsync().ConfigureAwait(false);

        if (_activeStrategyId == "ltg" && ListenTogetherManager.Instance?.ServerNextIndex is { } serverIndex)
        {
            ListenTogetherManager.Instance.ServerNextIndex = null;
            await MoveToIndexAsync(serverIndex).ConfigureAwait(false);
            return;
        }

        if (_activeStrategyId == "sgl" && NowPlayingProviderItem is not null && !userInitiated)
        {
            await _control.SeekAsync(TimeSpan.Zero).ConfigureAwait(false);
            _control.Play();
            return;
        }

        if (_activeStrategyId == "shn")
        {
            await MoveNextShuffleAsync().ConfigureAwait(false);
            return;
        }

        await _playCore.MoveNextAsync().ConfigureAwait(false);
        await LoadCurrentCoreSongAsync().ConfigureAwait(false);
    }

    public async Task MovePreviousAsync()
    {
        if (QueueCount == 0)
            return;

        if (_activeStrategyId == "shn")
        {
            await MovePreviousShuffleAsync().ConfigureAwait(false);
            return;
        }

        await _playCore.MovePreviousAsync().ConfigureAwait(false);
        await LoadCurrentCoreSongAsync().ConfigureAwait(false);
    }

    public Task MoveToAsync(ProvidableItemBase item)
    {
        var index = ProviderQueueSnapshot.ToList().FindIndex(providerItem => providerItem is not null
            && providerItem.ProviderId == item.ProviderId
            && providerItem.TypeId == item.TypeId
            && providerItem.ActualId == item.ActualId);

        return index >= 0 ? MoveToIndexAsync(index) : Task.CompletedTask;
    }

    public async Task MoveToIndexAsync(int index)
    {
        var snapshot = ProviderQueueSnapshot;
        if (index < 0 || index >= snapshot.Count)
            return;

        if (snapshot[index] is { } song)
            await _playCore.MovePointerToAsync(song).ConfigureAwait(false);

        await LoadCurrentCoreSongAsync().ConfigureAwait(false);
    }

    private async Task LoadCurrentCoreSongAsync()
    {
        await SyncIndexFromPlayCoreAsync().ConfigureAwait(false);
        if (_playCore.CurrentSong is { } song)
            await _control.LoadAndPlayAsync(song, removeCurrentSongs: false).ConfigureAwait(false);

        SendPlaylistChanged();
    }

    private async Task MoveNextShuffleAsync()
    {
        if (ShuffleList.Count != QueueCount)
            CreateShufflePlayLists();

        var nextShuffleIndex = ShufflingIndex + 1;
        if (nextShuffleIndex >= ShuffleList.Count)
            nextShuffleIndex = 0;

        await MoveToIndexAsync(ShuffleList[nextShuffleIndex]).ConfigureAwait(false);
    }

    private async Task MovePreviousShuffleAsync()
    {
        if (ShuffleList.Count != QueueCount)
            CreateShufflePlayLists();

        var previousShuffleIndex = ShufflingIndex - 1;
        if (previousShuffleIndex < 0)
            previousShuffleIndex = ShuffleList.Count - 1;

        await MoveToIndexAsync(ShuffleList[previousShuffleIndex]).ConfigureAwait(false);
    }

    private void ExitPersonalFmForSourceChange()
    {
        if (_state.IsInFm)
            PersonalFM.ExitFm(clearPlaylist: false);
    }

    internal async Task AppendMorePersonalFmTracksAsync()
    {
        var currentSong = NowPlayingProviderItem;
        var songs = _setting.useAiDj && currentSong is not null
            ? await LoadAiDjAsync(currentSong).ConfigureAwait(false)
            : await LoadPersonalFmAsync().ConfigureAwait(false);

        if (songs.Count > 0)
            AppendItems(songs);
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

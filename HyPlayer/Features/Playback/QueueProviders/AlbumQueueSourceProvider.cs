using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.QueueProviders;

/// <summary>
/// 专辑源提供者 — 加载 provider 专辑全部歌曲。
/// Prefix: "al", Kind: <see cref="SongListQueueScopeKind.Album"/>
/// </summary>
internal sealed class AlbumQueueSourceProvider : IQueueSourceProvider
{
    private readonly IProvidableItemProvidable _provider;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly INotificationService _notification;

    public AlbumQueueSourceProvider(IProvidableItemProvidable provider, IProviderKnownTypeIds knownTypeIds, INotificationService notification)
    {
        _provider = provider;
        _knownTypeIds = knownTypeIds;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Album;
    public string Prefix => QueueSourcePrefixes.Album;
    public bool SupportCompleteLoad => true;

    public async Task<ProviderQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var album = await _provider.GetProvidableItemByIdAsync(_knownTypeIds.AlbumTypeId + id, cancellationToken);
            var songs = album switch
            {
                LinerContainerBase linerAlbum => await linerAlbum.GetAllItemsAsync(cancellationToken),
                IProgressiveLoadingContainer progressiveAlbum => await LoadAllProgressiveItemsAsync(progressiveAlbum, cancellationToken),
                _ => null
            };

            return songs is null
                ? ProviderQueueSourceLoadResult.Failed
                : ProviderQueueSourceLoadResult.FromSongs(songs.OfType<SingleSongBase>().ToList());
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendAlbum时发生错误", ex.Message);
        }

        return ProviderQueueSourceLoadResult.Failed;
    }

    private static async Task<List<ProvidableItemBase>> LoadAllProgressiveItemsAsync(
        IProgressiveLoadingContainer container,
        CancellationToken cancellationToken)
    {
        var items = new List<ProvidableItemBase>();
        var offset = 0;
        var count = container.MaxProgressiveCount;
        var hasMore = true;

        while (hasMore)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await container.GetProgressiveItemsListAsync(offset, count, cancellationToken);
            hasMore = result.Item1;
            if (result.Item2.Count == 0)
                break;

            items.AddRange(result.Item2);
            offset += result.Item2.Count;
        }

        return items;
    }
}

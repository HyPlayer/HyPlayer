using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.QueueProviders;

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
            if (album is not LinerContainerBase linerAlbum)
                return ProviderQueueSourceLoadResult.Failed;

            var songs = await linerAlbum.GetAllItemsAsync(cancellationToken);
            return ProviderQueueSourceLoadResult.FromSongs(songs.OfType<SingleSongBase>().ToList());
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendAlbum时发生错误", ex.Message);
        }

        return ProviderQueueSourceLoadResult.Failed;
    }
}

using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Services.Abstractions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.QueueProviders;

/// <summary>
/// 专辑源提供者 — 加载网易云专辑全部歌曲。
/// Prefix: "al", Kind: <see cref="SongListQueueScopeKind.Album"/>
/// </summary>
internal sealed class AlbumQueueSourceProvider : IQueueSourceProvider
{
    private readonly IProvidableItemProvidable _provider;
    private readonly INotificationService _notification;

    public AlbumQueueSourceProvider(IProvidableItemProvidable provider, INotificationService notification)
    {
        _provider = provider;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Album;
    public string Prefix => QueueSourcePrefixes.Album;
    public bool SupportCompleteLoad => true;

    public async Task<NeteaseQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var album = await _provider.GetProvidableItemByIdAsync("al" + id, cancellationToken);
            if (album is not NeteaseAlbum neteaseAlbum)
                return NeteaseQueueSourceLoadResult.Failed;

            var songs = await neteaseAlbum.GetAllItemsAsync(cancellationToken);
            return NeteaseQueueSourceLoadResult.FromSongs(songs.OfType<SingleSongBase>().ToList());
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendAlbum时发生错误", ex.Message);
        }

        return NeteaseQueueSourceLoadResult.Failed;
    }
}

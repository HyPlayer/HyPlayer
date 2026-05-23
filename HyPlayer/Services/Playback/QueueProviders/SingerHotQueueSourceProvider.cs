using HyPlayer.Domain.Music;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.QueueProviders;

/// <summary>
/// 歌手热门歌曲源提供者 — 加载网易云歌手热门 Top 歌曲。
/// Prefix: "sa" (也兼容 "sh"), Kind: <see cref="SongListQueueScopeKind.Artist"/>
/// </summary>
internal sealed class SingerHotQueueSourceProvider : IQueueSourceProvider
{
    private readonly INotificationService _notification;

    public SingerHotQueueSourceProvider(INotificationService notification)
    {
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Artist;
    public string Prefix => QueueSourcePrefixes.Singer;
    public bool SupportCompleteLoad => false;

    public async Task<NeteaseQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = new NeteaseArtistSubContainer { ActualId = $"hot{id}", Name = id };
            var items = (await container.GetProgressiveItemsListAsync(0, 100, cancellationToken)).Item2;
            var songs = items.OfType<SingleSongBase>().Select(song => song.ToHyPlayItem().ToNCSong()).ToList();

            return songs.Count > 0
                ? NeteaseQueueSourceLoadResult.FromSongs(songs)
                : NeteaseQueueSourceLoadResult.Failed;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSource时发生错误", ex.Message);
        }

        return NeteaseQueueSourceLoadResult.Failed;
    }
}

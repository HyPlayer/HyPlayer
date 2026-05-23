using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
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
    private readonly IScopedItemRangeProvidable _provider;
    private readonly INotificationService _notification;

    public SingerHotQueueSourceProvider(IScopedItemRangeProvidable provider, INotificationService notification)
    {
        _provider = provider;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Artist;
    public string Prefix => QueueSourcePrefixes.Singer;
    public bool SupportCompleteLoad => false;

    public async Task<NeteaseQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await _provider.GetScopedItemsPageAsync(id, "ar", "sg", 0, 100, cancellationToken);
            var songs = page.Items.OfType<SingleSongBase>().Select(song => song.ToHyPlayItem().ToNCSong()).ToList();

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

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.QueueProviders;

/// <summary>
///     歌手热门歌曲源提供者 — 加载 provider 歌手热门 Top 歌曲。
///     Prefix: "sa" (也兼容 "sh"), Kind: <see cref="SongListQueueScopeKind.Artist" />
/// </summary>
internal sealed class SingerHotQueueSourceProvider : IQueueSourceProvider
{
    private readonly IContainerPageProvidable _containerPageProvider;
    private readonly INotificationService _notification;

    public SingerHotQueueSourceProvider(INotificationService notification,
        IContainerPageProvidable containerPageProvider)
    {
        _notification = notification;
        _containerPageProvider = containerPageProvider;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Artist;
    public string Prefix => QueueSourcePrefixes.Singer;
    public bool SupportCompleteLoad => false;

    public async Task<ProviderQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await _containerPageProvider.GetContainerItemsPageAsync($"{QueueSourcePrefixes.Singer}{id}", 0,
                100, cancellationToken);
            var songs = page.Items.OfType<SingleSongBase>().ToList();

            return songs.Count > 0
                ? ProviderQueueSourceLoadResult.FromSongs(songs)
                : ProviderQueueSourceLoadResult.Failed;
        }
        catch (Exception ex)
        {
            _notification.ShowFailure("加载歌手热门歌曲失败", ex);
        }

        return ProviderQueueSourceLoadResult.Failed;
    }
}
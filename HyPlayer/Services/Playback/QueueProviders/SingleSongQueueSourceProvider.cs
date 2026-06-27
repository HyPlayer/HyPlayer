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
/// 单曲源提供者 — 加载单首 provider 歌曲详情。
/// Prefix: "ns", Kind: <see cref="SongListQueueScopeKind.SingleSong"/>
/// </summary>
internal sealed class SingleSongQueueSourceProvider : IQueueSourceProvider
{
    private readonly IProvidableItemRangeProvidable _provider;
    private readonly INotificationService _notification;

    public SingleSongQueueSourceProvider(IProvidableItemRangeProvidable provider, INotificationService notification)
    {
        _provider = provider;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.SingleSong;
    public string Prefix => QueueSourcePrefixes.SingleSong;
    public bool SupportCompleteLoad => false;

    public async Task<ProviderQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var songs = await _provider.GetProvidableItemsRangeAsync(["sg" + id], cancellationToken);
            var song = songs.OfType<SingleSongBase>().FirstOrDefault();

            return song is not null
                ? ProviderQueueSourceLoadResult.FromSongs([song])
                : ProviderQueueSourceLoadResult.Failed;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("获取歌曲信息失败", ex.Message);
        }

        return ProviderQueueSourceLoadResult.Failed;
    }
}

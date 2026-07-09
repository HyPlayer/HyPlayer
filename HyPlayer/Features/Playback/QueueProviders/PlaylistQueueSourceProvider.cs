using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
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
/// 歌单源提供者 — 加载 provider 歌单全部歌曲。
/// Prefix: "pl", Kind: <see cref="SongListQueueScopeKind.Playlist"/>
/// </summary>
internal sealed class PlaylistQueueSourceProvider : IQueueSourceProvider
{
    private readonly IContainerPageProvidable _provider;
    private readonly INotificationService _notification;

    public PlaylistQueueSourceProvider(IContainerPageProvidable provider, INotificationService notification)
    {
        _provider = provider;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Playlist;
    public string Prefix => QueueSourcePrefixes.Playlist;
    public bool SupportCompleteLoad => true;

    public async Task<ProviderQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var batches = new List<IList<SingleSongBase>>();
            var offset = 0;
            const int count = 500;
            while (true)
            {
                var page = await _provider.GetContainerItemsPageAsync(id, offset, count, cancellationToken);
                var songs = page.Items.OfType<SingleSongBase>().ToList();
                if (songs.Count > 0)
                    batches.Add(songs);

                if (!page.HasMore || songs.Count == 0)
                    break;

                offset = page.NextOffset ?? offset + count;
            }

            if (batches.Count == 0)
            {
                _notification.ShowMessage("获取歌单失败", "歌曲详情为空或全部获取失败");
                return ProviderQueueSourceLoadResult.Failed;
            }

            return ProviderQueueSourceLoadResult.FromBatches(batches);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendPlayList时发生错误", ex.Message);
        }

        return ProviderQueueSourceLoadResult.Failed;
    }
}

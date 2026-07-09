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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.QueueProviders;

/// <summary>
/// 歌手热门歌曲源提供者 — 加载 provider 歌手热门 Top 歌曲。
/// Prefix: "sa" (也兼容 "sh"), Kind: <see cref="SongListQueueScopeKind.Artist"/>
/// </summary>
internal sealed class SingerHotQueueSourceProvider : IQueueSourceProvider
{
    private readonly INotificationService _notification;
    private readonly IContainerPageProvidable _containerPageProvider;

    public SingerHotQueueSourceProvider(INotificationService notification, IContainerPageProvidable containerPageProvider)
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
            var page = await _containerPageProvider.GetContainerItemsPageAsync($"{QueueSourcePrefixes.Singer}{id}", 0, 100, cancellationToken);
            var songs = page.Items.OfType<SingleSongBase>().ToList();

            return songs.Count > 0
                ? ProviderQueueSourceLoadResult.FromSongs(songs)
                : ProviderQueueSourceLoadResult.Failed;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSource时发生错误", ex.Message);
        }

        return ProviderQueueSourceLoadResult.Failed;
    }
}

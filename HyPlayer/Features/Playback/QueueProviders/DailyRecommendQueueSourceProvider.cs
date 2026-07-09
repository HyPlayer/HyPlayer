using HyPlayer.Domain.Music;
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.QueueProviders;

/// <summary>
/// 每日推荐源提供者。
/// Prefix: "dr", Kind: <see cref="SongListQueueScopeKind.DailyRecommend"/>
/// </summary>
internal sealed class DailyRecommendQueueSourceProvider : IQueueSourceProvider
{
    private readonly IProviderSpecialContainerTypeIds _specialTypeIds;
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly INotificationService _notification;

    public DailyRecommendQueueSourceProvider(
        IProviderSpecialContainerTypeIds specialTypeIds,
        IProvidableItemProvidable itemProvider,
        INotificationService notification)
    {
        _specialTypeIds = specialTypeIds;
        _itemProvider = itemProvider;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.DailyRecommend;
    public string Prefix => QueueSourcePrefixes.DailyRecommend;
    public bool SupportCompleteLoad => true;

    public async Task<ProviderQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_specialTypeIds.SpecialContainerTypeIds.TryGetValue(SpecialContainerType.RecommendedSongs, out var typeId))
                return ProviderQueueSourceLoadResult.Failed;

            if (await _itemProvider.GetProvidableItemByIdAsync(typeId + "rcsg", cancellationToken) is not ContainerBase container)
                return ProviderQueueSourceLoadResult.Failed;

            var items = await ContainerItemLoader.LoadAllAsync(container, cancellationToken);
            var songs = items.OfType<SingleSongBase>().ToList();
            return songs.Count > 0
                ? ProviderQueueSourceLoadResult.FromSongs(songs)
                : ProviderQueueSourceLoadResult.Failed;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("获取每日推荐失败", ex.Message);
        }

        return ProviderQueueSourceLoadResult.Failed;
    }
}

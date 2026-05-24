using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.Strategies;

/// <summary>
/// 私人 FM 策略：自动从网易云 FM 接口加载后续曲目。
/// <para>
/// 当播放列表即将耗尽时（当前索引接近末尾），OnTrackEnded 返回 <see cref="PlayStrategyAction.LoadMore"/>，
/// 由 PlaylistService 调用 <see cref="LoadMoreAsync"/> 获取新曲目追加到列表。
/// </para>
/// <para>
/// 支持普通 FM 模式和 AI DJ 模式，通过 <see cref="HyPlayer.App.Setting.useAiDj"/> 切换。
/// </para>
/// </summary>
public sealed class PersonalFmStrategy : IAsyncPlayStrategy
{
    private readonly Setting _setting;

    /// <summary>
    /// 创建私人 FM 策略实例
    /// </summary>
    /// <param name="setting">应用设置</param>
    public PersonalFmStrategy(Setting setting)
    {
        _setting = setting ?? throw new ArgumentNullException(nameof(setting));
    }

    /// <inheritdoc />
    public string Id => "pfm";

    /// <inheritdoc />
    public int? GetNext(PlayStrategyContext ctx)
    {
        if (ctx.Items.Count == 0) return null;
        var next = ctx.CurrentIndex + 1;
        return next < ctx.Items.Count ? next : null;
    }

    /// <inheritdoc />
    public int? GetPrevious(PlayStrategyContext ctx)
    {
        if (ctx.Items.Count == 0) return null;
        var prev = ctx.CurrentIndex - 1;
        return prev >= 0 ? prev : null;
    }

    /// <inheritdoc />
    public void OnPlaylistChanged(PlayStrategyContext ctx) { }

    /// <inheritdoc />
    public PlayStrategyAction OnTrackEnded(PlayStrategyContext ctx)
    {
        // 当接近列表末尾时请求加载更多
        if (ctx.CurrentIndex + 1 >= ctx.Items.Count)
            return PlayStrategyAction.LoadMore;

        return PlayStrategyAction.MoveNext;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SingleSongBase>> LoadMoreProviderItemsAsync(PlayStrategyContext ctx, CancellationToken ct = default)
    {
        if (!_setting.useAiDj)
            return await LoadPersonalFmAsync(ct).ConfigureAwait(false);

        return await LoadAiDjAsync(ctx, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从普通私人 FM 接口加载曲目
    /// </summary>
    private async Task<IEnumerable<SingleSongBase>> LoadPersonalFmAsync(CancellationToken ct)
    {
        return (await new NeteasePersonalFMContainer { ActualId = "default", Name = "私人 FM" }.GetNextItemsRangeAsync(ct).ConfigureAwait(false))
            .OfType<SingleSongBase>();
    }

    /// <summary>
    /// 从 AI DJ 接口加载曲目
    /// </summary>
    private async Task<IEnumerable<SingleSongBase>> LoadAiDjAsync(PlayStrategyContext ctx, CancellationToken ct)
    {
        if (ctx.CurrentProviderItem is { } currentSong)
        {
            var itemId = currentSong.ActualId ?? currentSong.Name;
            var container = new NeteaseContextRecommendationContainer
            {
                ActualId = itemId,
                SeedItemId = itemId,
                Name = "相关推荐",
                Count = 10
            };
            var items = await container.GetAllItemsAsync(ct).ConfigureAwait(false);

            var songs = items.OfType<SingleSongBase>().ToList();
            if (songs.Count > 0) return songs;
        }

        return await LoadPersonalFmAsync(ct).ConfigureAwait(false);
    }

}

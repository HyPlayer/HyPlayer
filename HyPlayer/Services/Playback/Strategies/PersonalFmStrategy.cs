using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Strategies;

/// <summary>
/// 私人 FM 策略：自动从网易云 FM 接口加载后续曲目。
/// <para>
/// 当播放列表即将耗尽时（当前索引接近末尾），OnTrackEnded 返回 <see cref="PlayStrategyAction.LoadMore"/>，
/// 由 PlaylistService 调用 <see cref="LoadMoreAsync"/> 获取新曲目追加到列表。
/// </para>
/// <para>
/// 支持普通 FM 模式和 AI DJ 模式，通过 <see cref="Setting.useAiDj"/> 切换。
/// </para>
/// </summary>
public sealed class PersonalFmStrategy : IAsyncPlayStrategy
{
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly Setting _setting;
    private bool _isNewToAiDj = true;

    /// <summary>
    /// 创建私人 FM 策略实例
    /// </summary>
    /// <param name="api">网易云音乐 API 处理器</param>
    /// <param name="setting">应用设置</param>
    public PersonalFmStrategy(NeteaseCloudMusicApiHandler api, Setting setting)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
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
    public async Task<IEnumerable<HyPlayItem>> LoadMoreAsync(PlayStrategyContext ctx, CancellationToken ct = default)
    {
        if (!_setting.useAiDj)
            return await LoadPersonalFmAsync(ct).ConfigureAwait(false);

        return await LoadAiDjAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从普通私人 FM 接口加载曲目
    /// </summary>
    private async Task<IEnumerable<HyPlayItem>> LoadPersonalFmAsync(CancellationToken ct)
    {
        var result = await _api.RequestAsync(NeteaseApis.PersonalFmApi, ct).ConfigureAwait(false);
        if (result.IsError || result.Value?.Items is not { Length: > 0 })
            return [];

        return result.Value.Items
            .Select(item => item.MapToNcSong())
            .Select(NcSongToHyPlayItem);
    }

    /// <summary>
    /// 从 AI DJ 接口加载曲目
    /// </summary>
    private async Task<IEnumerable<HyPlayItem>> LoadAiDjAsync(CancellationToken ct)
    {
        var result = await _api.RequestAsync(NeteaseApis.AiDjContentRcmdInfoApi,
            new AiDjContentRcmdInfoRequest { IsNewToAidj = _isNewToAiDj }, ct).ConfigureAwait(false);
        _isNewToAiDj = false;

        if (result.IsError || result.Value?.Data?.AiDjResources is not { Length: > 0 })
            return [];

        var items = new List<HyPlayItem>();
        foreach (var resource in result.Value.Data.AiDjResources)
        {
            switch (resource)
            {
                case AiDjContentRcmdInfoResponse.AiDjContentRcmdInfoData.AiDjContentRcmdAudioResource audioResource:
                    foreach (var audio in audioResource.Value?.Audio ?? [])
                    {
                        items.Add(new HyPlayItem
                        {
                            ItemType = HyPlayItemType.Netease,
                            Album = new NCAlbum
                            {
                                AlbumType = HyPlayItemType.Netease,
                                Alias = "私人 DJ",
                                Cover = "https://p1.music.126.net/kMuXXbwHbduHpLYDmHXrlA==/109951168152833223.jpg",
                                Description = "私人 DJ",
                                Id = "126368130",
                                Name = "私人 DJ 推荐语"
                            },
                            Artist =
                            [
                                new NCArtist
                                {
                                    Alias = "私人 DJ",
                                    Avatar = "https://p1.music.126.net/kMuXXbwHbduHpLYDmHXrlA==/109951168152833223.jpg",
                                    Id = "1",
                                    Name = "私人 DJ",
                                    Type = HyPlayItemType.Netease
                                }
                            ],
                            Bitrate = 0,
                            Id = audio.Id ?? "-1",
                            IsLocalFile = false,
                            LengthInMilliseconds = audio.Duration,
                            Name = "私人 DJ 推荐语",
                            InfoTag = "私人 DJ",
                            Url = audio.Url,
                            Size = audio.Size ?? 0
                        });
                    }
                    break;

                case AiDjContentRcmdInfoResponse.AiDjContentRcmdInfoData.AiDjContentRcmdAudioSong songResource:
                    var ncSong = songResource.Value?.SongName?.MapToNcSong();
                    if (ncSong is not null)
                        items.Add(NcSongToHyPlayItem(ncSong));
                    break;
            }
        }

        return items;
    }

    /// <summary>
    /// 将 NCSong 转换为 HyPlayItem（与 IPlaylistService.NCSongToPlayItem 逻辑一致）
    /// </summary>
    private static HyPlayItem NcSongToHyPlayItem(NCSong ncSong)
    {
        return new HyPlayItem
        {
            ItemType = ncSong.Type,
            InfoTag = ncSong.Alias,
            Album = ncSong.Album,
            Artist = ncSong.Artist,
            Id = ncSong.SongId,
            Translation = ncSong.TranslatedName,
            Name = ncSong.SongName,
            TrackId = ncSong.TrackId,
            CDName = ncSong.CDName,
            LengthInMilliseconds = ncSong.LengthInMilliseconds
        };
    }
}

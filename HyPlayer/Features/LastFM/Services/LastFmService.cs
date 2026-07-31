using System;
using System.Linq;
using System.Threading.Tasks;
using HyPlayer.Application.Notifications;
using HyPlayer.Classes;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using LiteFM;
using LiteFM.Abstractions.ApiContracts;
using LiteFM.Api;

namespace HyPlayer.Features.LastFM.Services;

public sealed class LastFmService : ILastFmService
{
    private readonly LastFMClient _client;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly INotificationService _notification;
    private readonly LastFMSettings _setting;

    public LastFmService(
        LastFMClient client,
        LastFMSettings setting,
        INotificationService notification,
        IProviderKnownTypeIds knownTypeIds)
    {
        _client = client;
        _setting = setting;
        _notification = notification;
        _knownTypeIds = knownTypeIds;
    }

    public Uri CreateLoginUri()
    {
        return new Uri(
            "https://www.last.fm/api/auth/?api_key=" + LastFMConstants.APIKEY + "&cb=hyplayer://link.last.fm");
    }

    public async Task CompleteBrowserLoginAsync(string token)
    {
        var response = await _client.RequestAsync(LastFMApi.GetSessionApi, new GetSessionRequest { Token = token });
        if (response.IsSuccess)
        {
            _setting.LastFMSession = response.Response.Session;
            return;
        }

        _notification.ShowMessage("Last.FM 登录失败", response.Error.Message);
    }

    public async Task UpdateNowPlayingAsync(SingleSongBase item)
    {
        var session = _setting.LastFMSession;
        if (!session.HasLogined || !_setting.UpdateLastFMNowPlaying || !IsSingleSong(item))
            return;

        var request = new UpdateNowPlayingRequest
        {
            Album = item.Album?.Name ?? string.Empty,
            Artist = item.CreatorList?.FirstOrDefault() ?? string.Empty,
            Track = item.Name
        };
        var response = await _client.RequestAsync(LastFMApi.UpdateNowPlayingApi, request, session);
        if (!response.IsSuccess)
            _notification.ShowMessage("Last.FM 上传正在播放失败", response.Error.Message);
    }

    public async Task ScrobbleAsync(SingleSongBase item)
    {
        var session = _setting.LastFMSession;
        if (!session.HasLogined || !_setting.LastFMScrobble || !IsSingleSong(item))
            return;

        var request = new ScrobbleRequest
        {
            Album = item.Album?.Name ?? string.Empty,
            Artist = item.CreatorList?.FirstOrDefault() ?? string.Empty,
            Track = item.Name,
            TimeStamp = (uint)(DateTime.UtcNow - DateTime.UnixEpoch - TimeSpan.FromMilliseconds(item.Duration))
                .TotalSeconds
        };
        var response = await _client.RequestAsync(LastFMApi.ScrobbleApi, request, session);
        if (!response.IsSuccess)
            _notification.ShowMessage("Last.FM 上传播放记录失败", response.Error.Message);
    }

    private bool IsSingleSong(SingleSongBase item)
    {
        return item.TypeId == _knownTypeIds.SingleSongTypeId;
    }
}
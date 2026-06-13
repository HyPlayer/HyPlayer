using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.NeteaseProvider.Constants;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Domain.Settings;
using HyPlayer.Services.Abstractions;
using LiteFM;
using LiteFM.Abstractions.ApiContracts;
using LiteFM.Api;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.LastFM
{
    public static class LastFMManager
    {
        public static async Task TryLoginLastfmAccountFromBrowser(string token)
        {
            var client = Ioc.Default.GetRequiredService<LastFMClient>();
            var response = await client.RequestAsync(LastFMApi.GetSessionApi, new GetSessionRequest() { Token = token });
            if (response.IsSuccess)
            {
                Ioc.Default.GetRequiredService<Setting>().LastFMSession = response.Response.Session;
            }
            else
            {
                var notification = Ioc.Default.GetRequiredService<INotificationService>();
                notification.ShowMessage("Last.FM 登录失败", response.Error.Message);
            }
        }
        public static async Task UpdateNowPlaying(SingleSongBase item)
        {
            var setting = Ioc.Default.GetRequiredService<Setting>();
            var session = setting.LastFMSession;
            if (!session.HasLogined || !setting.UpdateLastFMNowPlaying || item.ProviderId != "ncm" || item.TypeId != NeteaseTypeIds.SingleSong) return;
            var request = new UpdateNowPlayingRequest()
            {
                Album = item.Album?.Name ?? string.Empty,
                Artist = item.CreatorList?.FirstOrDefault() ?? string.Empty,
                Track = item.Name
            };
            var response = await Ioc.Default.GetRequiredService<LastFMClient>().RequestAsync(LastFMApi.UpdateNowPlayingApi, request, session);
            if (!response.IsSuccess)
            {
                var notification = Ioc.Default.GetRequiredService<INotificationService>();
                notification.ShowMessage("Last.FM 上传正在播放失败", response.Error.Message);
            }
        }
        public static async Task Scrobble(SingleSongBase item)
        {
            var setting = Ioc.Default.GetRequiredService<Setting>();
            var session = setting.LastFMSession;
            if (!session.HasLogined || !setting.LastFMScrobble || item.ProviderId != "ncm" || item.TypeId != NeteaseTypeIds.SingleSong) return;
            var request = new ScrobbleRequest()
            {
                Album = item.Album?.Name ?? string.Empty,
                Artist = item.CreatorList?.FirstOrDefault() ?? string.Empty,
                Track = item.Name,
                TimeStamp = (uint)(DateTime.UtcNow - DateTime.UnixEpoch - TimeSpan.FromMilliseconds(item.Duration)).TotalSeconds

            };
            var response = await Ioc.Default.GetRequiredService<LastFMClient>().RequestAsync(LastFMApi.ScrobbleApi, request, session);
            if (!response.IsSuccess)
            {
                var notification = Ioc.Default.GetRequiredService<INotificationService>();
                notification.ShowMessage("Last.FM 上传播放记录失败", response.Error.Message);
            }
        }
    }
}

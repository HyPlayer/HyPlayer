using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
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
            var response = await Ioc.Default.GetRequiredService<LastFMClient>().RequestAsync(LastFMApi.GetSessionApi, new GetSessionRequest() { Token = token });
            if (response.IsSuccess)
            {
                Ioc.Default.GetRequiredService<Setting>().LastFMSession = response.Response.Session;
            }
            else
            {
                Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("Last.FM 登录失败", response.Error.Message));
            }
        }
        public static async Task UpdateNowPlaying(HyPlayItem item)
        {
            if (!Ioc.Default.GetRequiredService<Setting>().LastFMSession.HasLogined || !Ioc.Default.GetRequiredService<Setting>().UpdateLastFMNowPlaying || item.ItemType != HyPlayItemType.Netease) return;
            var request = new UpdateNowPlayingRequest()
            {
                Album = item.AlbumString,
                Artist = item.Artist.FirstOrDefault()?.Name ?? string.Empty,
                Track = item.Name
            };
            var response = await Ioc.Default.GetRequiredService<LastFMClient>().RequestAsync(LastFMApi.UpdateNowPlayingApi, request, Ioc.Default.GetRequiredService<Setting>().LastFMSession);
            if (!response.IsSuccess)
            {
                Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("Last.FM 上传正在播放失败", response.Error.Message));
            }
        }
        public static async Task Scrobble(HyPlayItem item)
        {
            if (!Ioc.Default.GetRequiredService<Setting>().LastFMSession.HasLogined || !Ioc.Default.GetRequiredService<Setting>().LastFMScrobble || item.ItemType != HyPlayItemType.Netease) return;
            var request = new ScrobbleRequest()
            {
                Album = item.AlbumString,
                Artist = item.Artist.FirstOrDefault()?.Name ?? string.Empty,
                Track = item.Name,
                TimeStamp = (uint)(DateTime.UtcNow - DateTime.UnixEpoch - TimeSpan.FromMilliseconds(item.LengthInMilliseconds)).TotalSeconds

            };
            var response = await Ioc.Default.GetRequiredService<LastFMClient>().RequestAsync(LastFMApi.ScrobbleApi, request, Ioc.Default.GetRequiredService<Setting>().LastFMSession);
            if (!response.IsSuccess)
            {
                Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("Last.FM 上传播放记录失败", response.Error.Message));
            }
        }
    }
}

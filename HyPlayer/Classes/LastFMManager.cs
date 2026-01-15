using HyPlayer.HyPlayControl;
using LiteFM.Abstractions.ApiContracts;
using LiteFM.Api;
using NMeCab.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace HyPlayer.Classes
{
    public static class LastFMManager
    {
        public static async Task TryLoginLastfmAccountFromBrowser(string token)
        {
            var response = await Common.LastFMClient.RequestAsync(LastFMApi.GetSessionApi, new GetSessionRequest() { Token = token });
            if (response.IsSuccess)
            {
                Common.Setting.LastFMSession = response.Response.Session;
            }
            else
            {
                Common.AddToTeachingTipLists("Last.FM 登录失败", response.Error.Message);
            }
        }
        public static async Task UpdateNowPlaying(HyPlayItem item) 
        {
            if (!Common.Setting.LastFMSession.HasLogined || !Common.Setting.UpdateLastFMNowPlaying) return;
            var request = new UpdateNowPlayingRequest()
            {
                Album = item.PlayItem.AlbumString,
                Artist = item.PlayItem.Artist.FirstOrDefault()?.name ?? string.Empty,
                Track = item.PlayItem.Name
            };
            var response = await Common.LastFMClient.RequestAsync(LastFMApi.UpdateNowPlayingApi, request, Common.Setting.LastFMSession);
            if (!response.IsSuccess) 
            {
                Common.AddToTeachingTipLists("Last.FM 上传正在播放失败", response.Error.Message);
            }
        }
        public static async Task Scrobble(HyPlayItem item)
        {
            if (!Common.Setting.LastFMSession.HasLogined || !Common.Setting.LastFMScrobble) return;
            var request = new ScrobbleRequest()
            {
                Album = item.PlayItem.AlbumString,
                Artist = item.PlayItem.Artist.FirstOrDefault()?.name ?? string.Empty,
                Track = item.PlayItem.Name,
                TimeStamp = (uint)(DateTime.UtcNow - DateTime.UnixEpoch - TimeSpan.FromMilliseconds(item.PlayItem.LengthInMilliseconds)).TotalSeconds
                
            };
            var response = await Common.LastFMClient.RequestAsync(LastFMApi.ScrobbleApi, request, Common.Setting.LastFMSession);
            if (!response.IsSuccess)
            {
                Common.AddToTeachingTipLists("Last.FM 上传播放记录失败", response.Error.Message);
            }
        }
    }
}

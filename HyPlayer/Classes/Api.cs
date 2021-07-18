using System.Collections.Generic;
using NeteaseCloudMusicApi;

namespace HyPlayer.Classes
{
    internal class Api
    {
        public static async void LikeSong(string songid, bool like)
        {
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Like,
                new Dictionary<string, object> {{"id", songid}, {"like", like ? "true" : "false"}});
        }
    }
}
using NeteaseCloudMusicApi;
using System.Collections.Generic;

namespace HyPlayer.Classes
{
    internal class Api
    {
        public static async void LikeSong(string songid, bool like)
        {
            (bool isok, Newtonsoft.Json.Linq.JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Like,
                new Dictionary<string, object>() { { "id", songid }, { "like", like ? "true" : "false" } });
        }
    }
}

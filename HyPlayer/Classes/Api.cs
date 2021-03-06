using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeteaseCloudMusicApi;

namespace HyPlayer.Classes
{
    class Api
    {
        public static async void LikeSong(string songid, bool like)
        {
            var (isok,json) =await  Common.ncapi.RequestAsync(CloudMusicApiProviders.Like,
                new Dictionary<string, object>() {{"id", songid}, {"like", like ? "true" : "false"}});
        }
    }
}

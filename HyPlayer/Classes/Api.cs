#region

using System;
using System.Collections.Generic;
using NeteaseCloudMusicApi;

#endregion

namespace HyPlayer.Classes;

internal class Api
{
    public static async void LikeSong(string songid, bool like)
    {
        try
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.Like,
                new Dictionary<string, object> { { "id", songid }, { "like", like ? "true" : "false" } });
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }
}
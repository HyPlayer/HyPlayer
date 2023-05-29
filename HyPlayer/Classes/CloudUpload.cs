#region

using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Web.Http;
using Windows.Web.Http.Headers;

#endregion

namespace HyPlayer.Classes;

/// <summary>
///     网易云音乐云盘上载
///     @copyright Kengwang
///     @refer https://github.com/Binaryify/NeteaseCloudMusicApi
/// </summary>
internal class CloudUpload
{
    public static async Task<Dictionary<string, JObject>> UploadMusic(StorageFile file)
    {
        try
        {
            Common.AddToTeachingTipLists("上传本地音乐至音乐云盘中", "正在上传: " + file.DisplayName);
            //首先获取基本信息
            //var tagfile = File.Create(new UwpStorageFileAbstraction(file));
            var basicprop = await file.GetBasicPropertiesAsync();
            var musicprop = await file.Properties.GetMusicPropertiesAsync();
            var bytes = await FileIO.ReadBufferAsync(file);
            //再获取上传所需要的信息
            var computedHash = new MD5CryptoServiceProvider().ComputeHash(bytes.ToArray());
            var sBuilder = new StringBuilder();
            foreach (var b in computedHash) sBuilder.Append(b.ToString("x2").ToLower());

            var md5 = sBuilder.ToString();


            var res = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CloudUploadCheck,
                new Dictionary<string, object>
                {
                    { "bitrate", musicprop.Bitrate },
                    { "size", basicprop.Size },
                    { "md5", md5 }
                });


            var tokenres = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CloudUploadToken,
                new Dictionary<string, object>
                {
                    { "ext", file.FileType },
                    { "filename", file.Name },
                    { "md5", md5 }
                });

            if (res["needUpload"].ToObject<bool>())
            {
                // 文件需要上传
                var tokenRes = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CloudUploadToken,
                    new Dictionary<string, object>
                    {
                        { "ext", file.FileType },
                        { "filename", file.Name },
                        { "md5", md5 }
                    });
                var s = tokenRes["result"]["objectKey"].ToString() /*.Replace("/", "%2F")*/;
                var r = new Regex("\\/");
                var objkey = r.Replace(s, "%2F", 1);
                var targetLink = "http://45.127.129.8/jd-musicrep-privatecloud-audio-public/" + objkey + "?offset=0&complete=true&version=1.0";
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    new Uri(targetLink));
                using var fileStream = await file.OpenAsync(FileAccessMode.Read);
                using var content = new HttpStreamContent(fileStream);
                content.Headers.ContentLength = basicprop.Size;
                content.Headers.Add("Content-MD5", md5);
                request.Headers.Add("x-nos-token", tokenRes["result"]["token"].ToString());
                content.Headers.ContentType = new HttpMediaTypeHeaderValue(file.ContentType);
                request.Content = content;
                await Common.HttpClient.SendRequestAsync(request);
            }

            var title = string.IsNullOrEmpty(musicprop.Title)
                ? Path.GetFileNameWithoutExtension(file.Path)
                : musicprop.Title;
            var res2 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UploadCloudInfo,
                new Dictionary<string, object>
                {
                    { "md5", md5 },
                    { "songId", res["songId"].ToString() },
                    { "filename", file.Name },
                    { "song", title },
                    { "album", musicprop.Album },
                    { "artist", musicprop.Artist },
                    { "bitrate", musicprop.Bitrate },
                    { "resourceId", tokenres["result"]["resourceId"].ToString() }
                });

            var res3 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CloudPub,
                new Dictionary<string, object>
                {
                    { "songid", res2["songId"] }
                });
            Common.AddToTeachingTipLists("上传本地音乐至音乐云盘成功", "成功上传: " + file.DisplayName);
            return new Dictionary<string, JObject>
            {
                { "res", res },
                { "res3", res3 }
            };
        }
        catch (Exception e)
        {
            Common.AddToTeachingTipLists(e.Message, (e.InnerException ?? new Exception()).Message);
            return null;
        }
    }
}
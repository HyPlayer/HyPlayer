using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace HyPlayer.Classes
{
    /// <summary>
    /// 网易云音乐云盘上载
    /// @copyright Kengwang
    /// @refer https://github.com/Binaryify/NeteaseCloudMusicApi
    /// </summary>
    class CloudUpload
    {
        public static async Task<Dictionary<string, JObject>> UploadMusic(StorageFile file)
        {
            try
            {
                //首先获取基本信息
                //var tagfile = File.Create(new UwpStorageFileAbstraction(file));
                var basicprop = await file.GetBasicPropertiesAsync();
                var musicprop = await file.Properties.GetMusicPropertiesAsync();
                var bytes = await FileIO.ReadBufferAsync(file);
                //再获取上传所需要的信息
                byte[] computedHash = new MD5CryptoServiceProvider().ComputeHash(bytes.ToArray());
                var sBuilder = new StringBuilder();
                foreach (byte b in computedHash)
                {
                    sBuilder.Append(b.ToString("x2").ToLower());
                }

                string md5 = sBuilder.ToString();


                var res = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CloudUploadCheck,
                    new Dictionary<string, object>()
                    {
                        { "bitrate", musicprop.Bitrate },
                        { "size", basicprop.Size },
                        { "md5", md5 }
                    });


                var tokenres = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CloudUploadToken,
                    new Dictionary<string, object>()
                    {
                        { "ext", file.FileType },
                        { "filename", file.Name },
                        { "md5", md5 }
                    });

                if (res["needUpload"].ToObject<bool>())
                {
                    // 文件需要上传
                    var tokenRes = await Common.ncapi.RequestAsync(CloudMusicApiProviders.CloudUploadToken,
                        new Dictionary<string, object>()
                        {
                            { "ext", file.FileType },
                            { "filename", file.Name },
                            { "md5", md5 }
                        });
                    string s = tokenRes["result"]["objectKey"].ToString() /*.Replace("/", "%2F")*/;
                    Regex r = new Regex("\\/");
                    var objkey = r.Replace(s, "%2F", 1);
                    HttpClient request = new HttpClient();
                    var content = new StreamContent(await file.OpenStreamForReadAsync());
                    content.Headers.Add("x-nos-token", tokenRes["result"]["token"].ToString());
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
                    content.Headers.Add("Content-MD5", md5);
                    content.Headers.ContentLength = (long)basicprop.Size;

                    await request.PostAsync(
                        "http://45.127.129.8/ymusic/" + objkey + "?offset=0&complete=true&version=1.0",
                        content);
                }

                string title = string.IsNullOrEmpty(musicprop.Title)
                    ? Path.GetFileNameWithoutExtension(file.Path)
                    : musicprop.Title;
                var res2 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UploadCloudInfo,
                    new Dictionary<string, object>()
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
                    new Dictionary<string, object>()
                    {
                        { "songid", res2["songId"] }
                    });

                return new Dictionary<string, JObject>()
                {
                    { "res", res },
                    { "res3", res3 }
                };
            }
            catch (Exception e)
            {
                Common.ShowTeachingTip(e.Message, (e.InnerException ?? new Exception()).Message);
                return null;
            }
        }
    }
}
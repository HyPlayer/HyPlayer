#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.NeteaseApi;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Cloud;
using HyPlayer.Services.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

#endregion

namespace HyPlayer.Classes;

/// <summary>
///     网易云音乐云盘上载
///     @copyright Kengwang
///     @refer https://github.com/Binaryify/NeteaseCloudMusicApi
/// </summary>
internal class CloudUpload
{
#nullable enable
    public static async Task UploadMusic(StorageFile file)
    {
        var _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        var _notification = Ioc.Default.GetRequiredService<INotificationService>();
        var _httpClient = Ioc.Default.GetRequiredService<HttpClient>();
        _notification.ShowMessage("上传本地音乐至音乐云盘中", "正在上传: " + file.DisplayName);
        var musicprop = await file.Properties.GetMusicPropertiesAsync();
        //首先获取基本信息
        using var abstraction = new UwpStorageFileAbstraction(file);
        var album = string.Empty;
        var duration = (long)musicprop.Duration.TotalMilliseconds;
        var bitrate = musicprop.Bitrate;
        var name = file.DisplayName;
        var artist = string.Empty;
        byte[]? coverBytes = null;
        try
        {
            using var tagFile = TagLibHelper.Create(abstraction, file.FileType);
            var tag = tagFile?.Tag;
            album = tag?.Album;
            name = tag?.Title;
            artist = string.Join("; ", tag?.Performers ?? []);
            coverBytes = tag?.Pictures?.FirstOrDefault()?.Data?.Data;
        }
        catch
        {
            //Ignore
        }

        var bytes = await FileIO.ReadBufferAsync(file);
        //再获取上传所需要的信息
        var computedHash = MD5.HashData(bytes.ToArray());
        var sBuilder = new StringBuilder();
        foreach (var b in computedHash) sBuilder.Append(b.ToString("x2").ToLower());
        var md5 = sBuilder.ToString();
        var checkResult = await _api.RequestAsync(NeteaseApis.CloudUploadCheckApi,
            new CloudUploadCheckRequest()
            {
                Ext = file.FileType,
                Md5 = md5,
                Bitrate = (int)bitrate,
            });
        if (checkResult.IsError)
        {
            _notification.ShowMessage($"上传失败: {file.DisplayName}", checkResult.Error!.Message);
            return;
        }
        var title = string.IsNullOrEmpty(name)
            ? Path.GetFileNameWithoutExtension(file.Path)
            : name;
        var infoReq = new CloudUploadInfoRequest
        {
            Md5 = md5,
            SongId = checkResult.Value!.SongId!,
            FileName = file.Name,
            Song = title ?? file.DisplayName,
            Album = album ?? "",
            Artist = artist,
            Bitrate = (int)bitrate
        };
        var lb = "http://45.127.129.8";
        if (checkResult.Value?.NeedUpload is not false)
        {
            // 文件需要上传
            var tokenRequest = new CloudUploadTokenAllocRequest
            {
                FileName = file.Name,
                Md5 = md5
            };
            var tokenRes = await _api.RequestAsync(NeteaseApis.CloudUploadTokenAllocApi, tokenRequest);
            if (tokenRes.IsError)
            {
                _notification.ShowMessage($"上传失败: {file.DisplayName}", tokenRes.Error!.Message);
                return;
            }

            var objkey = tokenRes.Value!.Data!.ObjectKey;
            // fetch load balancer
            var loadBalancerReq = new NeteaseUploadLoadBalancerGetRequest()
            {
                Bucket = "jd-musicrep-privatecloud-audio-public"
            };
            var loadBalancerRes = await _api.RequestAsync(NeteaseApis.NeteaseUploadLoadBalancerGetApi,
                loadBalancerReq);
            if (loadBalancerRes.IsSuccess)
            {
                lb = loadBalancerRes.Value!.Upload?.FirstOrDefault() ?? lb;
            }

            var targetLink = $"{lb}/jd-musicrep-privatecloud-audio-public/{objkey}?version=1.0";
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(targetLink));
            using var fileStream = await file.OpenAsync(FileAccessMode.Read);
            using var stream = fileStream.AsStream();
            await UploadToNos(targetLink, stream, md5, tokenRes.Value.Data.Token, file.ContentType);
            infoReq.ResourceId = tokenRes.Value.Data!.ResourceId!;
            infoReq.ObjectKey = $"jd-musicrep-privatecloud-audio-public/{tokenRes.Value.Data!.ObjectKey}";
        }

        string coverId = string.Empty;
        // upload cover
        if (coverBytes != null)
        {
            var imgcomputedHash = MD5.HashData(coverBytes);
            var imgsBuilder = new StringBuilder();
            foreach (var b in imgcomputedHash) imgsBuilder.Append(b.ToString("x2").ToLower());
            var imgmd5 = imgsBuilder.ToString();

            var coverAllocRes = await _api.RequestAsync(NeteaseApis.CloudUploadCoverTokenAllocApi,
                new CloudUploadCoverTokenAllocRequest
                {
                    Ext = "png",
                    Filename = $"{file.DisplayName}_cover",
                });
            if (coverAllocRes.IsError)
            {
                _notification.ShowMessage($"上传失败(封面): {file.DisplayName}", coverAllocRes.Error!.Message);
            }
            coverId = coverAllocRes.Value?.Result?.DocId!;
            var imglb = "http://45.127.129.8";
            var imgloadBalancerReq = new NeteaseUploadLoadBalancerGetRequest()
            {
                Bucket = "yyimgs"
            };
            var imgloadBalancerRes = await _api.RequestAsync(NeteaseApis.NeteaseUploadLoadBalancerGetApi,
                imgloadBalancerReq);
            if (imgloadBalancerRes.IsSuccess)
            {
                imglb = imgloadBalancerRes.Value!.Upload?.FirstOrDefault() ?? imglb;
            }
            var targetLink = $"{imglb}/yyimgs/{coverAllocRes.Value?.Result?.ObjectKey}?version=1.0";
            using var imgStream = new MemoryStream(coverBytes);
            await UploadToNos(targetLink, imgStream, imgmd5, coverAllocRes.Value?.Result?.Token, "image/png");
            infoReq.CoverId = coverId;
        }



        var infoRes = await _api.RequestAsync(NeteaseApis.CloudUploadInfoApi, infoReq);
        if (infoRes.IsError)
        {
            _notification.ShowMessage($"上传失败: {file.DisplayName}", infoRes.Error!.Message);
            return;
        }
        var cloudPubReq = new CloudPubRequest()
        {
            SongId = infoRes.Value!.SongId!
        };
        var cloudPubRes = await _api.RequestAsync(NeteaseApis.CloudPubApi, cloudPubReq);
        if (cloudPubRes.IsError)
        {
            _notification.ShowMessage($"上传失败: {file.DisplayName}", cloudPubRes.Error!.Message);
        }
        else
        {
            _notification.ShowMessage("上传本地音乐至音乐云盘成功", "成功上传: " + file.DisplayName);
        }

    }


    public static async Task UploadToNos(string targetLink, Stream stream, string md5, string? token, string contentType, int chunkSize = 1048576, CancellationToken cancellationToken = default)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable", nameof(stream));
        if (string.IsNullOrEmpty(token)) throw new ArgumentException("Token must not be null or empty");
        {

        }

        try
        {
            // seek to beginning if possible
            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            string? context = null;

            var isEnd = false;
            int offset = 0;

            while (!isEnd && !cancellationToken.IsCancellationRequested)
            {
                // Read chunk
                var buffer = new byte[chunkSize];
                var bytesRead = await stream.ReadAsync(buffer, 0, chunkSize, cancellationToken);
                isEnd = bytesRead < chunkSize;

                if (bytesRead == 0) break;

                // Create request
                using var req = new HttpRequestMessage(
                    HttpMethod.Post,
                    new Uri($"{targetLink}&offset={offset * chunkSize}&complete={isEnd.ToString().ToLower()}&context={context}"));

                using var content = new ByteArrayContent(buffer, 0, bytesRead);
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                content.Headers.Add("Content-MD5", md5); // Consider calculating MD5 per chunk instead
                req.Headers.Add("x-nos-token", token);
                req.Content = content;

                // Send request
                using var resp = await Ioc.Default.GetRequiredService<HttpClient>().SendAsync(req, cancellationToken);

                if (!resp.IsSuccessStatusCode)
                {
                    var errorContent = await resp.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Upload failed with status {resp.StatusCode}: {errorContent}");
                }

                var rs = await resp.Content.ReadAsStringAsync();
                // get context in "context":"", using regex
                var match = System.Text.RegularExpressions.Regex.Match(rs, "\"context\"\\s*:\\s*\"([^\"]*)\"");
                if (match.Success)
                {
                    context = match.Groups[1].Value;
                }
                offset++;
            }
        }
        catch (OperationCanceledException)
        {
            //Ignore
        }
    }

#nullable restore
}
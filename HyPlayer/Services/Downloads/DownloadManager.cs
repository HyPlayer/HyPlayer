#region

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Audio;
using HyPlayer.Infrastructure.Extensions;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TagLib;
using Windows.Graphics.Imaging;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using UwpStorageFileAbstraction = HyPlayer.Infrastructure.Audio.UwpStorageFileAbstraction;

#endregion

namespace HyPlayer.Services.Downloads;

internal sealed partial class DownloadObject : ObservableObject
{
    public HyPlayItem PlayItem;
    private DownloadOperation _downloadOperation;
    private readonly ITeachingTipService _teachingTipService;
    private readonly Setting _setting;
    private readonly HttpClient _httpClient;
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly IDiagnosticsStateService _diagnostics;
    private readonly NCSong _song;

    private IStorageFile _resultFileBackingField;
    public IStorageFile ResultFile
    {
        get => _resultFileBackingField ?? _downloadOperation.ResultFile;
        set
        {
            _resultFileBackingField = value;
        }
    }
    [ObservableProperty]
    public partial string FileName { get; set; }

    public string FullPath { get; set; }
    [ObservableProperty]

    public partial ulong HadSize { get; set; }

    public NCSong ncsong;

    public DownloadObject(
        NCSong song,
        ITeachingTipService teachingTipService,
        Setting setting,
        HttpClient httpClient,
        NeteaseCloudMusicApiHandler api,
        IDiagnosticsStateService diagnostics)
    {
        _teachingTipService = teachingTipService;
        _setting = setting;
        _httpClient = httpClient;
        _api = api;
        _diagnostics = diagnostics;
        _song = song;
        ncsong = song;
    }

    [ObservableProperty]
    public partial int Progress { get; set; }

    public enum DownloadStatus
    {
        Queueing,
        Downloading,
        Finished,
        Paused,
        Error
    }

    // 0 - 排队 1 - 下载中 2 - 下载完成  3 - 暂停
    public DownloadStatus Status { get; set; }
    [ObservableProperty]
    public partial bool HasError { get; set; }
    [ObservableProperty]
    public partial bool HasPaused { get; set; }
    [ObservableProperty]
    public partial string Message { get; set; }
    [ObservableProperty]
    public partial ulong TotalSize { get;set; }

    public void Pause()
    {
        if (_downloadOperation is { Progress.Status: BackgroundTransferStatus.Running })
            _downloadOperation?.Pause();
        Status = DownloadStatus.Paused;
        Message = "暂停中";
        HasPaused = true;
        HasError = false;
    }

    public void Resume()
    {
        _downloadOperation?.Resume();
        Status = DownloadStatus.Downloading;
        Message = "下载中";
        HasPaused = false;
    }

    public void Remove()
    {
        if (_downloadOperation is { Progress.Status: BackgroundTransferStatus.Running })
            _downloadOperation?.Pause();
        Status = DownloadStatus.Finished;
        Message = "已移除";
        HasPaused = false;
    }

    private void Wc_DownloadFileCompleted()
    {
        DownloadManager.WritingTasks.Add(Task.Run(async () =>
        {
            if (_setting.downloadLyric)
                await DownloadLyric().ConfigureAwait(false);
            if (_setting.writedownloadFileInfo)
                await WriteInfoToFile().ConfigureAwait(false);
            DownloadManager.WritingTasks.RemoveAll(t => t.IsCompleted);
            Status = DownloadStatus.Finished;
        }));
        Message = "下载完成";
    }

    private Task WriteInfoToFile()
    {
        Message = "正在写文件信息";
        return Task.Run(async () =>
        {
            using var streamAbstraction = new UwpStorageFileAbstraction(ResultFile);
            using var file = TagLibHelper.Create(streamAbstraction, ResultFile.FileType);
            try
            {
                if (_setting.write163Info && PlayItem is not null)
                    The163KeyHelper.TrySetMusicInfo(file.Tag, PlayItem);
                //写相关信息
                file.Tag.Album = ncsong.Album.Name;
                file.Tag.Performers = [.. ncsong.Artist.Select(t => t.Name)];
                file.Tag.Title = ncsong.SongName;
                file.Tag.Track = (uint)(ncsong.TrackId == -1 ? ncsong.Order + 1 : ncsong.TrackId);

                // 获取 Disc Id
                var regexRet = DiscInfoRegex().Match(ncsong.CDName ?? "01");
                if (regexRet.Success)
                {
                    file.Tag.Disc = uint.Parse(regexRet.Value);
                }
                else
                {
                    file.Tag.Disc = 1;
                }

                //file.Save();

                Picture pic;
                using var responseMessage = await _httpClient.GetAsync(new Uri(ncsong.Album.Cover + "?param=" +
                                                                        StaticSource.PICSIZE_DOWNLOAD_ALBUMCOVER));
                using IRandomAccessStream outputStream = new InMemoryRandomAccessStream();
                using var stream = await responseMessage.Content.ReadAsStreamAsync();
                using var inputStream = stream.AsRandomAccessStream();
                SoftwareBitmap softwareBitmap;
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(inputStream);
                softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                BitmapEncoder encoder =
                    await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outputStream);
                encoder.SetSoftwareBitmap(softwareBitmap);
                await encoder.FlushAsync();
                pic = new Picture(ByteVector.FromStream(outputStream.AsStreamForRead()));
                DownloadManager.AlbumPicturesCache[ncsong.Album.Id] = pic;

                file.Tag.Pictures =
                [
                    pic
                ];
                file.Tag.Pictures[0].MimeType = "image/jpeg";
                file.Tag.Pictures[0].Description = "Cover.jpg";
                file.Save();
            }
            catch (Exception ex)
            {
                Status = DownloadStatus.Error;
                HasError = true;
                HasPaused = true;
                Progress = 100;
                Message = "写入音乐信息时出现错误" + ex.Message;
                _diagnostics.ErrorMessages.Add("写入音乐信息时出现错误" + ex.Message);
            }
        });
    }

    private Task DownloadLyric()
    {
        Message = "下载歌词中";
        //下载歌词
        return Task.Run(async () =>
        {
            var lyricRequest = new LyricRequest() { Id = ncsong.SongId };
            var lyricResult = await _api.RequestAsync(NeteaseApis.LyricApi, lyricRequest);
            if (lyricResult.IsSuccess)
            {
                var data = lyricResult.Value;
                if (data.Lyric == null) return;
                if (data.Lyric.Lyric == "[99:00.00]纯音乐，请欣赏") return;
                var lrc = Utils.ConvertPureLyric(data.Lyric.Lyric);
                if (_setting.downloadTranslation && data.TranslationLyric != null)
                {
                    Utils.ConvertTranslation(data.TranslationLyric.Lyric, lrc);
                }
                var lrctxt = string.Join("\r\n", lrc.Select(t =>
                {
                    if (t.HaveTranslation && !string.IsNullOrWhiteSpace(t.Translation))
                        return "[" + t.LyricLine.StartTime.ToString(@"mm\:ss\.ff") + "]" + t.LyricLine.CurrentLyric + " 「" +
                               t.Translation + "」";
                    return "[" + t.LyricLine.StartTime.ToString(@"mm\:ss\.ff") + "]" + t.LyricLine.CurrentLyric;
                }));
                if (string.IsNullOrWhiteSpace(lrctxt)) return;
                var sf = await (await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(FullPath)))
                    .CreateFileAsync(
                        Path.GetFileName(Path.ChangeExtension(FullPath, "lrc")),
                        CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(sf, lrctxt);
            }
            else
            {
                Status = DownloadStatus.Error;
                Message = "下载歌词错误: " + lyricResult.Error.Message;
                HasError = true;
                HasPaused = true;
                Progress = 100;
            }
        });
    }

    private static string GetSize(double size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        const double mod = 1024.0;
        var i = 0;
        while (size >= mod)
        {
            size /= mod;
            i++;
        }

        return Math.Round(size, 2) + units[i];
    }

    private void Wc_DownloadProgressChanged(DownloadOperation obj)
    {
        if (obj.Progress.TotalBytesToReceive == 0) return;
        if (Status != DownloadStatus.Downloading) return;
        TotalSize = obj.Progress.TotalBytesToReceive;
        HadSize = obj.Progress.BytesReceived;
        Progress = (int)(obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive);
        Message = $"下载中: {GetSize(obj.Progress.BytesReceived)} / {GetSize(obj.Progress.TotalBytesToReceive)}";
        if (HadSize == TotalSize && Status == DownloadStatus.Finished) return;
    }
    public async Task StartDownload()
    {
        if (_downloadOperation != null) { Resume(); return; }
        Status = DownloadStatus.Downloading;
        HasError = false;
        HasPaused = false;
        Message = "正在预加载";
        try
        {
            FileName = _setting.downloadFileName
                .Replace("{$SINGER}", string.Join(';', ncsong.Artist.Select(t => t.Name)).EscapeForPath())
                .Replace("{$SONGNAME}", ncsong.SongName.EscapeForPath())
                .Replace("{$ALBUM}", ncsong.Album.Name.EscapeForPath())
                .Replace("{$INDEX}",
                    (ncsong.GetType() == typeof(NCAlbumSong) ? ncsong.Order : ncsong.Order + 1).ToString().EscapeForPath())
                .Replace("{$CDNAME}", ncsong.CDName?.EscapeForPath())
                .Replace("{$SONGID}", ncsong.SongId?.EscapeForPath());
            var folderName = _setting.downloadDir;
            var nowFolder = await StorageFolder.GetFolderFromPathAsync(folderName);
            var ses = FileName.Replace('\\', '/').Split('/');
            for (var index = 0; index < ses.Length - 1; index++)
            {
                var s = ses[index];
                folderName += "/" + s;
                nowFolder = await nowFolder.CreateFolderAsync(s, CreationCollisionOption.OpenIfExists);
            }

            if (await nowFolder.FileExistsAsync(Path.GetFileName(FileName + ".mp3")) ||
                await nowFolder.FileExistsAsync(Path.GetFileName(FileName + ".flac")))
                switch (_setting.downloadNameOccupySolution)
                {
                    case OccupySolution.Skip:
                        Status = DownloadStatus.Paused;
                        Message = "歌曲已存在, 跳过";
                        return;
                    case OccupySolution.ReWrite:
                        await (await nowFolder.GetFileAsync(Path.GetFileName(FileName))).DeleteAsync();
                        break;
                    case OccupySolution.AppendID:
                        FileName = Path.GetFileNameWithoutExtension(FileName) + ncsong.SongId;
                        break;
                    case OccupySolution.UpdateInfo:
                        if (await nowFolder.FileExistsAsync(Path.GetFileName(FileName + ".mp3")))
                        {
                            ResultFile = await nowFolder.GetFileAsync(Path.GetFileName(FileName + ".mp3"));
                        }
                        if (await nowFolder.FileExistsAsync(Path.GetFileName(FileName + ".flac")))
                        {
                            ResultFile = await nowFolder.GetFileAsync(Path.GetFileName(FileName + ".flac"));
                        }
                        FullPath = ResultFile.Path;
                        Wc_DownloadFileCompleted();
                        return;
                }
            HasError = false;
            HasPaused = false;
            Message = "正在获取下载链接";
            var urlRequest = new SongUrlRequest() { Id = ncsong.SongId, Level = _setting.downloadAudioRate };
            var urlResult = await _api.RequestAsync(NeteaseApis.SongUrlApi, urlRequest);

            if (urlResult.IsError || urlResult.Value?.SongUrls?[0] is null)
            {
                Status = DownloadStatus.Error;
                Message = "获取下载链接错误";
                HasError = true;
                HasPaused = true;
                Progress = 100;
                return;
            }

            if (urlResult.Value.SongUrls[0].FreeTrialInfo is not null && _setting.jumpVipSongDownloading)
            {
                Status = DownloadStatus.Paused;
                HasPaused = true;
                Progress = 100;
                Message = "VIP 试听歌曲, 跳过";
                return;
            }

            FileName += "." + urlResult.Value.SongUrls[0].Type?.ToLowerInvariant();
            PlayItem = ncsong.ToHyPlayItem();
            PlayItem.Bitrate = Convert.ToInt32(urlResult.Value.SongUrls[0].BitRate);
            PlayItem.QualityTag = "下载";
            PlayItem.InfoTag = "下载";
            PlayItem.SubExt = urlResult.Value.SongUrls[0].Type.ToLowerInvariant();
            PlayItem.Url = urlResult.Value.SongUrls[0].Url;
            PlayItem.Size = urlResult.Value.SongUrls[0].Size;

            _downloadOperation = DownloadManager.Downloader.CreateDownload(
                new Uri(urlResult.Value.SongUrls[0].Url),
                await nowFolder.CreateFileAsync(Path.GetFileName(FileName))
            );
            FullPath = _downloadOperation.ResultFile.Path;
            //_downloadOperation.IsRandomAccessRequired = true;
            var process = new Progress<DownloadOperation>(Wc_DownloadProgressChanged);
            //DownloadStartToast(FileName);
            await _downloadOperation.StartAsync().AsTask(process);
            Wc_DownloadFileCompleted();
        }
        catch (Exception ex)
        {
            Status = DownloadStatus.Error;
            Message = "下载错误: " + ex.Message;
            _diagnostics.ErrorMessages.Add("无法下载歌曲 " + ncsong.SongName + "\n已自动将其从下载列表中移除" + ex.Message);
        }
    }

    [GeneratedRegex("[0-9]+")]
    private static partial Regex DiscInfoRegex();
}

internal static class DownloadManager
{
    private static bool Timered;
    private static IGlobalTimerService GlobalTimer => Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private static ITeachingTipService teachingTipService => Ioc.Default.GetRequiredService<ITeachingTipService>();
    private static Setting Setting => Ioc.Default.GetRequiredService<Setting>();
    private static HttpClient HttpClient => Ioc.Default.GetRequiredService<HttpClient>();
    private static NeteaseCloudMusicApiHandler Api => Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
    private static IDiagnosticsStateService Diagnostics => Ioc.Default.GetRequiredService<IDiagnosticsStateService>();
    public static ObservableCollection<DownloadObject> DownloadLists = [];
    public static BackgroundDownloader Downloader = new();
    public static List<Task> WritingTasks = [];
    public static Dictionary<string, Picture> AlbumPicturesCache = [];

    public static bool CheckDownloadAbilityAndToast()
    {
        teachingTipService.Items.Enqueue(new ("开始下载", null));
        return true;
    }

    private static void EnsureTimerStarted()
    {
        if (!Timered)
        {
            GlobalTimer.SecondTick += Timer_Elapsed;
            Timered = true;
        }
    }

    public static void StopTimer()
    {
        if (Timered)
        {
            GlobalTimer.SecondTick -= Timer_Elapsed;
            Timered = false;
        }
        WritingTasks.RemoveAll(t => t.IsCompleted);
        if (DownloadLists.Count == 0)
            AlbumPicturesCache.Clear();
    }

    public static void AddDownload(NCSong song)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        EnsureTimerStarted();

        DownloadLists.Add(CreateDownloadObject(song));
    }

    private static void Timer_Elapsed(object? sender, EventArgs e)
    {
        Timer_Elapsed();
    }

    private static void Timer_Elapsed()
    {
        if (DownloadLists.Count == 0)
        {
            StopTimer();
            return;
        }
        var maxDownloadCount = Setting.maxDownloadCount;
        for (var i = 0; i < DownloadLists.Count; i++)
        {
            switch (DownloadLists[i].Status)
            {
                case DownloadObject.DownloadStatus.Downloading:
                    if (--maxDownloadCount <= 0) return;
                    continue;
                case DownloadObject.DownloadStatus.Queueing:
                    _ = DownloadLists[i].StartDownload();
                    --maxDownloadCount;
                    return;
                case DownloadObject.DownloadStatus.Finished:
                    var i1 = i;
                    DownloadLists.RemoveAt(i1);
                    if (DownloadLists.Count == 0)
                        StopTimer();
                    break;
                case DownloadObject.DownloadStatus.Paused:
                case DownloadObject.DownloadStatus.Error:
                    break;
            }
        }
    }

    public static void AddDownload(List<NCSong> songs)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        EnsureTimerStarted();

        songs.ForEach(t => { DownloadLists.Add(CreateDownloadObject(t)); });
    }

    private static DownloadObject CreateDownloadObject(NCSong song)
    {
        return new DownloadObject(song, teachingTipService, Setting, HttpClient, Api, Diagnostics);
    }
}


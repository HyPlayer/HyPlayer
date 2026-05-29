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
using System.Threading;
using System.Threading.Tasks;
using TagLib;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Imaging;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using UwpStorageFileAbstraction = HyPlayer.Infrastructure.Audio.UwpStorageFileAbstraction;

#endregion

namespace HyPlayer.Services.Downloads;

public sealed partial class DownloadObject : ObservableObject
{
    public HyPlayItem PlayItem;
    private DownloadOperation _downloadOperation;
    private readonly Setting _setting;
    private readonly HttpClient _httpClient;
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly IDiagnosticsStateService _diagnostics;
    private readonly IBackgroundTaskRunner _taskRunner;

    private IStorageFile _resultFileBackingField;
    public IStorageFile ResultFile
    {
        get => _resultFileBackingField ?? _downloadOperation.ResultFile;
        set
        {
            _resultFileBackingField = value;
        }
    }

    public string FullPath { get; set; }

    public NCSong Song;

    public DownloadObject(
        NCSong song,
        Setting setting,
        HttpClient httpClient,
        NeteaseCloudMusicApiHandler api,
        IDiagnosticsStateService diagnostics,
        IBackgroundTaskRunner taskRunner)
    {
        _setting = setting;
        _httpClient = httpClient;
        _api = api;
        _diagnostics = diagnostics;
        Song = song;
        _taskRunner = taskRunner;
    }

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
    public partial string FileName { get; set; }
    [ObservableProperty]
    public partial ulong HadSize { get; set; }
    [ObservableProperty]
    public partial int Progress { get; set; }
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
        RunOnUIThread(() =>
        {
            Message = "暂停中";
            HasPaused = true;
            HasError = false;
        });
    }

    public void Resume()
    {
        _downloadOperation?.Resume();
        Status = DownloadStatus.Downloading;
        RunOnUIThread(() =>
        {
            Message = "下载中";
            HasPaused = false;
        });
    }

    public void Remove()
    {
        if (_downloadOperation is { Progress.Status: BackgroundTransferStatus.Running })
            _downloadOperation?.Pause();
        Status = DownloadStatus.Finished;
        RunOnUIThread(() =>
        {
            Message = "已移除";
            HasPaused = false;
        });
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
        RunOnUIThread(() =>
        {
            Message = "下载完成";
        });
    }

    private Task WriteInfoToFile()
    {
        RunOnUIThread(() =>
        {
            Message = "正在写文件信息";
        });
        return Task.Run(async () =>
        {
            using var streamAbstraction = new UwpStorageFileAbstraction(ResultFile);
            using var file = TagLibHelper.Create(streamAbstraction, ResultFile.FileType);
            try
            {
                if (_setting.write163Info && PlayItem is not null)
                    The163KeyHelper.TrySetMusicInfo(file.Tag, PlayItem);
                //写相关信息
                file.Tag.Album = Song.Album.Name;
                file.Tag.Performers = [.. Song.Artist.Select(t => t.Name)];
                file.Tag.Title = Song.SongName;
                file.Tag.Track = (uint)(Song.TrackId == -1 ? Song.Order + 1 : Song.TrackId);

                // 获取 Disc Id
                var regexRet = DiscInfoRegex().Match(Song.CDName ?? "01");
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
                using var responseMessage = await _httpClient.GetAsync(new Uri(Song.Album.Cover + "?param=" +
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
                DownloadManager.AlbumPicturesCache[Song.Album.Id] = pic;

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
                RunOnUIThread(() =>
                {
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                    Message = "写入音乐信息时出现错误" + ex.Message;
                });
                _diagnostics.ErrorMessages.Add("写入音乐信息时出现错误" + ex.Message);
            }
        });
    }

    private Task DownloadLyric()
    {
        RunOnUIThread(() =>
        {
            Message = "下载歌词中";
        });
        //下载歌词
        return Task.Run(async () =>
        {
            var lyricRequest = new LyricRequest() { Id = Song.SongId };
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
                RunOnUIThread(() =>
                {
                    Message = "下载歌词错误: " + lyricResult.Error.Message;
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                });
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
        RunOnUIThread(() =>
        {
            TotalSize = obj.Progress.TotalBytesToReceive;
            HadSize = obj.Progress.BytesReceived;
            Progress = (int)(obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive);
            Message = $"下载中: {GetSize(obj.Progress.BytesReceived)} / {GetSize(obj.Progress.TotalBytesToReceive)}";
        });
        if (HadSize == TotalSize && Status == DownloadStatus.Finished) return;
    }
    public async Task StartDownload()
    {
        if (_downloadOperation != null) { Resume(); return; }
        Status = DownloadStatus.Downloading;
        RunOnUIThread(() =>
        {
            HasError = false;
            HasPaused = false;
            Message = "正在预加载";
        });
        try
        {
            FileName = _setting.downloadFileName
                .Replace("{$SINGER}", string.Join(';', Song.Artist.Select(t => t.Name)).EscapeForPath())
                .Replace("{$SONGNAME}", Song.SongName.EscapeForPath())
                .Replace("{$ALBUM}", Song.Album.Name.EscapeForPath())
                .Replace("{$INDEX}",
                    (Song.GetType() == typeof(NCAlbumSong) ? Song.Order : Song.Order + 1).ToString().EscapeForPath())
                .Replace("{$CDNAME}", Song.CDName?.EscapeForPath())
                .Replace("{$SONGID}", Song.SongId?.EscapeForPath());
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
                        RunOnUIThread(() =>
                        {
                            Message = "歌曲已存在, 跳过";
                        });
                        return;
                    case OccupySolution.ReWrite:
                        await (await nowFolder.GetFileAsync(Path.GetFileName(FileName))).DeleteAsync();
                        break;
                    case OccupySolution.AppendID:
                        FileName = Path.GetFileNameWithoutExtension(FileName) + Song.SongId;
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
            RunOnUIThread(() =>
            {
                Message = "正在获取下载链接";
            });
            var urlRequest = new SongUrlRequest() { Id = Song.SongId, Level = _setting.downloadAudioRate };
            var urlResult = await _api.RequestAsync(NeteaseApis.SongUrlApi, urlRequest);

            if (urlResult.IsError || urlResult.Value?.SongUrls?[0] is null)
            {
                Status = DownloadStatus.Error;
                RunOnUIThread(() =>
                {
                    Message = "获取下载链接错误";
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                });
                return;
            }

            if (urlResult.Value.SongUrls[0].FreeTrialInfo is not null && _setting.jumpVipSongDownloading)
            {
                Status = DownloadStatus.Paused;
                RunOnUIThread(() =>
                {
                    HasPaused = true;
                    Progress = 100;
                    Message = "VIP 试听歌曲, 跳过";
                });
                return;
            }

            FileName += "." + urlResult.Value.SongUrls[0].Type?.ToLowerInvariant();
            PlayItem = Song.ToHyPlayItem();
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
            RunOnUIThread(() =>
            {
                Message = "下载错误: " + ex.Message;
            });
            _diagnostics.ErrorMessages.Add("无法下载歌曲 " + Song.SongName + "\n已自动将其从下载列表中移除" + ex.Message);
        }
    }

    [GeneratedRegex("[0-9]+")]
    private static partial Regex DiscInfoRegex();
    private void RunOnUIThread(Action action)
    {
        _taskRunner.Forget(
            CoreApplication.MainView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { action(); }),
            "DownloadObject Update");
    }
}

internal static class DownloadManager
{
    private static bool Timered;
    private static readonly IGlobalTimerService GlobalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private static readonly ITeachingTipService teachingTipService = Ioc.Default.GetRequiredService<ITeachingTipService>();
    private static readonly Setting Setting = Ioc.Default.GetRequiredService<Setting>();
    private static readonly HttpClient HttpClient = Ioc.Default.GetRequiredService<HttpClient>();
    private static readonly NeteaseCloudMusicApiHandler Api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
    private static readonly IDiagnosticsStateService Diagnostics = Ioc.Default.GetRequiredService<IDiagnosticsStateService>();
    private static readonly IBackgroundTaskRunner TaskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    public static ObservableCollection<DownloadObject> DownloadLists = [];
    public static BackgroundDownloader Downloader = new();
    public static List<Task> WritingTasks = [];
    public static Dictionary<string, Picture> AlbumPicturesCache = [];
    private static int CurrentDownloadCount;
    private static int MaxDownloadCount => Setting.maxDownloadCount;
    private static Lock _lock = new Lock();

    public static bool CheckDownloadAbilityAndToast()
    {
        teachingTipService.Enqueue(new ("开始下载", null));
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
        lock (_lock)
        {
            if (DownloadLists.Count == 0)
            {
                StopTimer();
                return;
            }
            for (var i = 0; i < DownloadLists.Count; i++)
            {
                var item = DownloadLists[i];
                switch (item.Status)
                {
                    case DownloadObject.DownloadStatus.Downloading:
                        if (MaxDownloadCount - CurrentDownloadCount <= 0) return;
                        continue;
                    case DownloadObject.DownloadStatus.Queueing:
                        CurrentDownloadCount++;
                        TaskRunner.Forget(item.StartDownload, "Start DownloadObject");
                        return;
                    case DownloadObject.DownloadStatus.Finished:
                        RunOnUIThread(() => { DownloadLists.Remove(item); });
                        CurrentDownloadCount--;
                        if (DownloadLists.Count == 0)
                            StopTimer();
                        break;
                    case DownloadObject.DownloadStatus.Paused:
                    case DownloadObject.DownloadStatus.Error:
                        break;
                }
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
        return new DownloadObject(song, Setting, HttpClient, Api, Diagnostics, TaskRunner);
    }
    private static void RunOnUIThread(Action action)
    {
        TaskRunner.Forget(
            CoreApplication.MainView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { action(); }),
            "DownloadManager Update");
    }
}


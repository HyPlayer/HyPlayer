#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Platform.Storage.Audio;
using HyPlayer.Platform.Xaml;
using HyPlayer.NeteaseProvider.LocalMusic;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Lyric;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
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
using UwpStorageFileAbstraction = HyPlayer.Platform.Storage.Audio.UwpStorageFileAbstraction;

#endregion

namespace HyPlayer.Features.Downloads.Services;

public sealed partial class DownloadObject : INotifyPropertyChanged
{
    private DownloadOperation _downloadOperation;
    private readonly INotificationService _notification;
    private readonly Setting _setting;
    private readonly HttpClient _httpClient;
    private readonly ILyricProvidable _lyricProvider;
    private readonly IReadOnlyList<IMusicResourceProvidable> _musicResourceProviders;
    private readonly IResourceQualityTagProvidable _qualityTagProvider;
    private readonly IDiagnosticsStateService _diagnostics;
    private readonly SingleSongBase _providerSong;
    private readonly string _downloadAlbumName;
    private readonly string[] _downloadArtistNames;
    private readonly string _downloadSongName;
    private readonly int _downloadOrder;
    private readonly int _downloadTrackId;
    private readonly string? _downloadCdName;
    private readonly string _downloadSongId;
    private readonly string? _downloadAlbumId;
    private readonly string? _downloadAlbumCover;
    private int _downloadBitrate;
    private string _downloadFormat;

    private IStorageFile _resultFileBackingField;
    public IStorageFile ResultFile
    {
        get => _resultFileBackingField ?? _downloadOperation.ResultFile;
        set
        {
            _resultFileBackingField = value;
        }
    }

    public string FileName
    {
        get => _fileName;
        set => SetField(ref _fileName, value);
    }

    public string FullPath { get; set; }

    public string SongName => _downloadSongName;

    public string ArtistText => string.Join(';', _downloadArtistNames);

    public string AlbumName => _downloadAlbumName;

    public string? AlbumCover => _downloadAlbumCover;

    public ulong HadSize
    {
        get => _hadSize;
        set => SetField(ref _hadSize, value);
    }

    public DownloadObject(
        SingleSongBase song,
        INotificationService notification,
        Setting setting,
        HttpClient httpClient,
        ILyricProvidable lyricProvider,
        IEnumerable<IMusicResourceProvidable> musicResourceProviders,
        IResourceQualityTagProvidable qualityTagProvider,
        IDiagnosticsStateService diagnostics)
    {
        _notification = notification;
        _setting = setting;
        _httpClient = httpClient;
        _lyricProvider = lyricProvider;
        _musicResourceProviders = musicResourceProviders?.ToList() ?? throw new ArgumentNullException(nameof(musicResourceProviders));
        _qualityTagProvider = qualityTagProvider;
        _diagnostics = diagnostics;
        _providerSong = song;
        _downloadAlbumName = song.Album?.Name ?? string.Empty;
        _downloadArtistNames = GetProviderArtistNames(song);
        _downloadSongName = song.Name ?? string.Empty;
        _downloadOrder = 0;
        var trackMetadata = song as IHasTrackMetadata;
        _downloadTrackId = trackMetadata?.TrackNumber ?? 0;
        _downloadCdName = trackMetadata?.DiscName;
        _downloadSongId = song.ActualId ?? string.Empty;
        _downloadAlbumId = song.Album?.ActualId;
        _downloadAlbumCover = GetProviderAlbumCover(song);
    }

    public int Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
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

    public bool HasError
    {
        get => _hasError;
        set => SetField(ref _hasError, value);
    }

    public bool HasPaused
    {
        get => _hasPaused;
        set => SetField(ref _hasPaused, value);
    }

    public string Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }


    private ulong _totalSize;
    private int _progress;
    private ulong _hadSize;
    private string _fileName;
    private string _message;
    private bool _hasError;
    private bool _hasPaused;

    public ulong TotalSize
    {
        get => _totalSize;
        set => SetField(ref _totalSize, value);
    }

    public void Pause()
    {
        if (_downloadOperation is { Progress.Status: BackgroundTransferStatus.Running })
            _downloadOperation?.Pause();
        Status = DownloadStatus.Paused;
        _ = _notification.InvokeOnUIThread(() =>
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
        _ = _notification.InvokeOnUIThread(() =>
        {
            Message = "下载中";
            HasPaused = false;
        });
    }

    public void Queue()
    {
        Status = DownloadStatus.Queueing;
        _ = _notification.InvokeOnUIThread(() =>
        {
            Message = "排队中";
            HasPaused = false;
            HasError = false;
        });
    }

    public void Retry()
    {
        Progress = 0;
        HadSize = 0;
        Queue();
    }

    public void Remove()
    {
        if (_downloadOperation is { Progress.Status: BackgroundTransferStatus.Running })
            _downloadOperation?.Pause();
        Status = DownloadStatus.Finished;
        _ = _notification.InvokeOnUIThread(() =>
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
        _ = _notification.InvokeOnUIThread(() => Message = "下载完成");
    }

    private Task WriteInfoToFile()
    {
        _ = _notification.InvokeOnUIThread(() => Message = "正在写文件信息");
        return Task.Run(async () =>
        {
            using var streamAbstraction = new UwpStorageFileAbstraction(ResultFile);
            using var file = TagLibHelper.Create(streamAbstraction, ResultFile.FileType);
            try
            {
                if (_setting.write163Info)
                    The163KeyHelper.TrySetMusicInfo(file.Tag, _providerSong, _downloadBitrate, _downloadFormat);
                //写相关信息
                file.Tag.Album = _downloadAlbumName;
                file.Tag.Performers = _downloadArtistNames;
                file.Tag.Title = _downloadSongName;
                file.Tag.Track = (uint)(_downloadTrackId == -1 ? _downloadOrder + 1 : _downloadTrackId);

                // 获取 Disc Id
                var regexRet = DiscInfoRegex().Match(_downloadCdName ?? "01");
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
                if (!string.IsNullOrWhiteSpace(_downloadAlbumCover))
                {
                    if (!string.IsNullOrWhiteSpace(_downloadAlbumId)
                        && DownloadManager.AlbumPicturesCache.TryGetValue(_downloadAlbumId, out var cachedPic))
                    {
                        pic = cachedPic;
                    }
                    else
                    {
                        using var responseMessage = await _httpClient.GetAsync(new Uri(_downloadAlbumCover + "?param=" +
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
                    }

                    if (!string.IsNullOrWhiteSpace(_downloadAlbumId))
                        DownloadManager.CacheAlbumPicture(_downloadAlbumId, pic);

                    file.Tag.Pictures =
                    [
                        pic
                    ];
                    file.Tag.Pictures[0].MimeType = "image/jpeg";
                    file.Tag.Pictures[0].Description = "Cover.jpg";
                }
                file.Save();
            }
            catch (Exception ex)
            {
                Status = DownloadStatus.Error;
                _ = _notification.InvokeOnUIThread(() =>
                {
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                    Message = "写入音乐信息时出现错误" + ex.Message;
                });
                _diagnostics.ErrorMessages.Add("写入音乐信息时出现错误" + ex.Message);
                _notification.ShowMessage("写入信息错误: " + ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        });
    }

    private Task DownloadLyric()
    {
        _ = _notification.InvokeOnUIThread(() => Message = "下载歌词中");
        //下载歌词
        return Task.Run(async () =>
        {
            try
            {
                var lyrics = await _lyricProvider.GetLyricInfoAsync(_providerSong);
                var original = await GetLyricTextAsync(lyrics, LyricType.Original, word: false);
                if (string.IsNullOrWhiteSpace(original)) return;
                if (original == "[99:00.00]纯音乐，请欣赏") return;
                var lrc = Utils.ConvertPureLyric(original);
                var translation = await GetLyricTextAsync(lyrics, LyricType.Translation, word: false);
                if (_setting.downloadTranslation && !string.IsNullOrWhiteSpace(translation))
                {
                    Utils.ConvertTranslation(translation, lrc);
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
            catch (Exception ex)
            {
                Status = DownloadStatus.Error;
                _ = _notification.InvokeOnUIThread(() =>
                {
                    Message = "下载歌词错误: " + ex.Message;
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                });
                _notification.ShowMessage("下载歌词错误: " + ex.Message);
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

        _ = _notification.InvokeOnUIThread((() =>
        {
            TotalSize = obj.Progress.TotalBytesToReceive;
            HadSize = obj.Progress.BytesReceived;
            Progress = (int)(obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive);
            Message = $"下载中: {GetSize(obj.Progress.BytesReceived)} / {GetSize(obj.Progress.TotalBytesToReceive)}";
        }));

        if (HadSize == TotalSize && Status == DownloadStatus.Finished) return;
    }

    public void DownloadStartToast(string SongName)
    {
        _notification.ShowMessage("下载开始", "歌曲" + SongName + "下载开始");
    }

    public async Task StartDownload()
    {
        if (_downloadOperation != null) { Resume(); return; }
        Status = DownloadStatus.Downloading;
        _ = _notification.InvokeOnUIThread(() =>
        {
            HasError = false;
            HasPaused = false;
            Message = "正在预加载";
        });
        try
        {
            FileName = _setting.downloadFileName
                .Replace("{$SINGER}", string.Join(';', _downloadArtistNames).EscapeForPath())
                .Replace("{$SONGNAME}", _downloadSongName.EscapeForPath())
                .Replace("{$ALBUM}", _downloadAlbumName.EscapeForPath())
                .Replace("{$INDEX}",
                    (_downloadTrackId > 0 ? _downloadTrackId : _downloadOrder + 1).ToString().EscapeForPath())
                .Replace("{$CDNAME}", _downloadCdName?.EscapeForPath())
                .Replace("{$SONGID}", _downloadSongId.EscapeForPath());
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
                        _ = _notification.InvokeOnUIThread(() => { Message = "歌曲已存在, 跳过"; });
                        return;
                    case OccupySolution.ReWrite:
                        await (await nowFolder.GetFileAsync(Path.GetFileName(FileName))).DeleteAsync();
                        break;
                    case OccupySolution.AppendID:
                        FileName = Path.GetFileNameWithoutExtension(FileName) + _downloadSongId;
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
            _ = _notification.InvokeOnUIThread(() =>
            {
                HasError = false;
                HasPaused = false;
                Message = "正在获取下载链接";
            });
            var qualityTags = await _qualityTagProvider.GetAvailableQualityTagsAsync(ResourceType.Audio);
            qualityTags.TryGetValue(_setting.downloadAudioRate, out var qualityTag);
            var musicResourceProvider = _musicResourceProviders.FirstOrDefault(provider => provider.Id == _providerSong.ProviderId);
            if (musicResourceProvider is null)
            {
                Status = DownloadStatus.Error;
                _ = _notification.InvokeOnUIThread(() =>
                {
                    Message = "未找到歌曲下载源";
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                });
                return;
            }

            var musicResource = await musicResourceProvider.GetMusicResourceAsync(_providerSong, qualityTag);

            if (musicResource?.Uri is null)
            {
                Status = DownloadStatus.Error;
                _ = _notification.InvokeOnUIThread(() =>
                {
                    Message = "获取下载链接错误";
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                });
                return;
            }

            var extension = (musicResource.ExtensionName ?? "mp3")
                .ToLowerInvariant();
            FileName += "." + extension;
            _downloadBitrate = 0;
            _downloadFormat = extension;

            _downloadOperation = DownloadManager.Downloader.CreateDownload(
                musicResource.Uri,
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
            _ = _notification.InvokeOnUIThread(() => { Message = "下载错误: " + ex.Message; });
            _diagnostics.ErrorMessages.Add("无法下载歌曲 " + _downloadSongName + "\n已自动将其从下载列表中移除" + ex.Message);
        }
    }

    private static string[] GetProviderArtistNames(SingleSongBase song)
    {
        return song.CreatorList?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? [];
    }

    private static string? GetProviderAlbumCover(SingleSongBase song)
    {
        var coverProvider = song.Album as IHasCover ?? song as IHasCover;
        if (coverProvider is null)
            return null;

        var result = coverProvider.GetCoverAsync().GetAwaiter().GetResult();
        return result is IResourceResultOf<Uri?> uriResult
            ? uriResult.GetResourceAsync().GetAwaiter().GetResult()?.ToString()
            : null;
    }

    private static async Task<string?> GetLyricTextAsync(IEnumerable<RawLyricInfo> lyrics, LyricType type, bool word)
    {
        foreach (var lyric in lyrics)
        {
            var isWord = lyric.Source?.Contains("yrc", StringComparison.OrdinalIgnoreCase) is true;
            if (isWord != word || lyric.LyricType != type)
                continue;

            var result = await lyric.GetResourceAsync();
            if (result is not IResourceResultOf<string> textResult)
                continue;

            var text = await textResult.GetResourceAsync();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    [GeneratedRegex("[0-9]+")]
    private static partial Regex DiscInfoRegex();
}

internal static class DownloadManager
{
    private static bool Timered;
    private static IGlobalTimerService GlobalTimer => Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private static INotificationService Notification => Ioc.Default.GetRequiredService<INotificationService>();
    private static Setting Setting => Ioc.Default.GetRequiredService<Setting>();
    private static HttpClient HttpClient => Ioc.Default.GetRequiredService<HttpClient>();
    private static ILyricProvidable LyricProvider => Ioc.Default.GetRequiredService<ILyricProvidable>();
    private static IReadOnlyList<IMusicResourceProvidable> MusicResourceProviders => global::HyPlayer.AppDepository.ResolveMultiple<IMusicResourceProvidable>();
    private static IResourceQualityTagProvidable QualityTagProvider => Ioc.Default.GetRequiredService<IResourceQualityTagProvidable>();
    private static IDiagnosticsStateService Diagnostics => Ioc.Default.GetRequiredService<IDiagnosticsStateService>();
    public static ObservableCollection<DownloadObject> DownloadLists = [];
    public static BackgroundDownloader Downloader = new();
    public static List<Task> WritingTasks = [];
    public static Dictionary<string, Picture> AlbumPicturesCache = [];
    private const int MaxAlbumPicturesCacheSize = 64;

    public static bool CheckDownloadAbilityAndToast()
    {
        Notification.ShowMessage("开始下载");
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
        TrimAlbumPicturesCache();
    }

    public static void AddDownload(SingleSongBase song)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        CleanupCompletedWritingTasks();
        EnsureTimerStarted();

        DownloadLists.Add(CreateDownloadObject(song));
    }

    public static void AddDownload(List<SingleSongBase> songs)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        CleanupCompletedWritingTasks();
        EnsureTimerStarted();

        songs.ForEach(t => { DownloadLists.Add(CreateDownloadObject(t)); });
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
                    _ = Notification.InvokeOnUIThread(() =>
                    {
                        DownloadLists.RemoveAt(i1);
                        if (DownloadLists.Count == 0)
                            StopTimer();
                    });
                    break;
                case DownloadObject.DownloadStatus.Paused:
                case DownloadObject.DownloadStatus.Error:
                    break;
            }
        }
    }

    private static DownloadObject CreateDownloadObject(SingleSongBase song)
    {
        return new DownloadObject(song, Notification, Setting, HttpClient, LyricProvider, MusicResourceProviders, QualityTagProvider, Diagnostics);
    }

    public static void CacheAlbumPicture(string albumId, Picture picture)
    {
        if (string.IsNullOrWhiteSpace(albumId))
            return;

        AlbumPicturesCache[albumId] = picture;
        TrimAlbumPicturesCache();
    }

    private static void CleanupCompletedWritingTasks()
    {
        WritingTasks.RemoveAll(t => t.IsCompleted);
    }

    private static void TrimAlbumPicturesCache()
    {
        while (AlbumPicturesCache.Count > MaxAlbumPicturesCacheSize)
            AlbumPicturesCache.Remove(AlbumPicturesCache.Keys.First());
    }
}

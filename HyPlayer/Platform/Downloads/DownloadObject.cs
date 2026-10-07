#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Application;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.Threading;
using HyPlayer.Domain;
using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.LocalMusic;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Storage.Audio;
using HyPlayer.Platform.Xaml;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Lyric;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using Microsoft.Toolkit.Uwp.Helpers;
using TagLib;
using UwpStorageFileAbstraction = HyPlayer.Platform.Storage.Audio.UwpStorageFileAbstraction;

#endregion

namespace HyPlayer.Features.Downloads.Services;

public sealed partial class DownloadObject : ObservableObject
{
    public enum DownloadStatus
    {
        Queueing,
        Downloading,
        Finished,
        Paused,
        Error,
        Processing
    }

    private readonly IDiagnosticsStateService _diagnostics;
    private readonly string? _downloadAlbumId;
    private readonly string[] _downloadArtistNames;
    private readonly string? _downloadCdName;
    private readonly int _downloadOrder;
    private readonly string _downloadSongId;
    private readonly int _downloadTrackId;
    private readonly HttpClient _httpClient;
    private readonly ILyricProvidable _lyricProvider;
    private readonly IReadOnlyList<IMusicResourceProvidable> _musicResourceProviders;
    private readonly INotificationService _notification;
    private readonly SingleSongBase _providerSong;
    private readonly IResourceQualityTagProvidable _qualityTagProvider;
    private readonly DownloadSettings _downloadSettings;
    private readonly LyricSettings _lyricSettings;
    private readonly IUIThreadDispatcher _uiThreadDispatcher;
    private int _downloadBitrate;
    private string _downloadFormat;
    private DownloadOperation _downloadOperation;
    private string _fileName;
    private ulong _hadSize;
    private bool _hasError;
    private bool _hasPaused;
    private string _message;
    private int _progress;
    private long _lastProgressTimestamp;
    private int _progressDispatchPending;
    private int _startInProgress;
    private volatile bool _removed;
    private int _status;
    private bool _transferCompleted;
    private bool _processingFailed;
    private readonly CancellationTokenSource _downloadCancellation = new();

    private IStorageFile _resultFileBackingField;


    private ulong _totalSize;

    public DownloadObject(
        SingleSongBase song,
        INotificationService notification,
        IUIThreadDispatcher uiThreadDispatcher,
        DownloadSettings downloadSettings,
        LyricSettings lyricSettings,
        HttpClient httpClient,
        ILyricProvidable lyricProvider,
        IEnumerable<IMusicResourceProvidable> musicResourceProviders,
        IResourceQualityTagProvidable qualityTagProvider,
        IDiagnosticsStateService diagnostics)
    {
        _notification = notification;
        _uiThreadDispatcher = uiThreadDispatcher;
        _downloadSettings = downloadSettings;
        _lyricSettings = lyricSettings;
        _httpClient = httpClient;
        _lyricProvider = lyricProvider;
        _musicResourceProviders = musicResourceProviders?.ToList() ??
                                  throw new ArgumentNullException(nameof(musicResourceProviders));
        _qualityTagProvider = qualityTagProvider;
        _diagnostics = diagnostics;
        _providerSong = song;
        AlbumName = song.Album?.Name ?? string.Empty;
        _downloadArtistNames = GetProviderArtistNames(song);
        SongName = song.Name ?? string.Empty;
        _downloadOrder = 0;
        var trackMetadata = song as IHasTrackMetadata;
        _downloadTrackId = trackMetadata?.TrackNumber ?? 0;
        _downloadCdName = trackMetadata?.DiscName;
        _downloadSongId = song.ActualId ?? string.Empty;
        _downloadAlbumId = string.IsNullOrWhiteSpace(song.Album?.ActualId)
            ? null
            : song.ProviderId + ":" + song.Album.ActualId;
        AlbumCover = null;
    }

    public IStorageFile ResultFile
    {
        get => _resultFileBackingField ?? _downloadOperation.ResultFile;
        set => _resultFileBackingField = value;
    }

    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    public string FullPath { get; set; }

    public string SongName { get; }

    public string ArtistText => string.Join(';', _downloadArtistNames);

    public string AlbumName { get; }

    public string? AlbumCover { get; private set; }

    public ulong HadSize
    {
        get => _hadSize;
        set => SetProperty(ref _hadSize, value);
    }

    public int Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    // 0 - 排队 1 - 下载中 2 - 下载完成  3 - 暂停
    public DownloadStatus Status
    {
        get => (DownloadStatus)Volatile.Read(ref _status);
        set => Volatile.Write(ref _status, (int)value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public bool HasPaused
    {
        get => _hasPaused;
        set => SetProperty(ref _hasPaused, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public ulong TotalSize
    {
        get => _totalSize;
        set => SetProperty(ref _totalSize, value);
    }

    public void Pause()
    {
        if (Status == DownloadStatus.Processing) return;
        if (_downloadOperation is { Progress.Status: BackgroundTransferStatus.Running })
            _downloadOperation?.Pause();
        Status = DownloadStatus.Paused;
        _ = _uiThreadDispatcher.TryRunAsync(() =>
        {
            Message = "暂停中";
            HasPaused = true;
            HasError = false;
        });
    }

    public void Resume()
    {
        Queue();
    }

    public void Queue()
    {
        if (_removed || Status is DownloadStatus.Processing or DownloadStatus.Finished) return;
        Status = DownloadStatus.Queueing;
        _ = _uiThreadDispatcher.TryRunAsync(() =>
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
        _removed = true;
        _downloadCancellation.Cancel();
        Status = DownloadStatus.Finished;
        _ = _uiThreadDispatcher.TryRunAsync(() =>
        {
            Message = "已移除";
            HasPaused = false;
        });
    }

    private void Wc_DownloadFileCompleted()
    {
        if (_removed) return;
        _transferCompleted = true;
        _processingFailed = false;
        Status = DownloadStatus.Processing;
        var task = Task.Run(async () =>
        {
            try
            {
                if (_lyricSettings.DownloadLyric)
                    await DownloadLyric().ConfigureAwait(false);
                if (_downloadSettings.WriteDownloadFileInfo)
                    await WriteInfoToFile().ConfigureAwait(false);
                if (_processingFailed && !_removed)
                    Status = DownloadStatus.Error;
                else if (!_removed)
                {
                    Status = DownloadStatus.Finished;
                    await _uiThreadDispatcher.TryRunAsync(() =>
                    {
                        Progress = 100;
                        Message = "下载完成";
                    });
                }
            }
            catch (Exception ex)
            {
                if (_removed) return;
                Status = DownloadStatus.Error;
                await _uiThreadDispatcher.TryRunAsync(() =>
                {
                    HasError = true;
                    HasPaused = true;
                    Message = "下载后处理错误: " + ex.Message;
                });
                _diagnostics.AddError("下载后处理错误: " + ex.Message);
            }
            finally
            {
                DownloadManager.CleanupCompletedWritingTasks();
            }
        });
        DownloadManager.RegisterWritingTask(task);
    }

    private Task WriteInfoToFile()
    {
        _ = _uiThreadDispatcher.TryRunAsync(() => Message = "正在写文件信息");
        return Task.Run(async () =>
        {
            try
            {
                using var streamAbstraction = await UwpStorageFileAbstraction.OpenAsync(ResultFile, writable: true);
                using var file = TagLibHelper.Create(streamAbstraction, "." + _downloadFormat);
                if (_downloadSettings.Write163Info)
                    The163KeyHelper.TrySetMusicInfo(file.Tag, _providerSong, _downloadBitrate, _downloadFormat);
                //写相关信息
                file.Tag.Album = AlbumName;
                file.Tag.Performers = _downloadArtistNames;
                file.Tag.Title = SongName;
                file.Tag.Track = (uint)(_downloadTrackId == -1 ? _downloadOrder + 1 : _downloadTrackId);

                // 获取 Disc Id
                var regexRet = DiscInfoRegex().Match(_downloadCdName ?? "01");
                if (regexRet.Success)
                    file.Tag.Disc = uint.Parse(regexRet.Value);
                else
                    file.Tag.Disc = 1;

                //file.Save();

                Picture pic;
                if (!string.IsNullOrWhiteSpace(AlbumCover))
                {
                    if (!string.IsNullOrWhiteSpace(_downloadAlbumId)
                        && DownloadManager.TryGetAlbumPicture(_downloadAlbumId, out var cachedPic))
                    {
                        pic = cachedPic;
                    }
                    else
                    {
                        using var responseMessage = await _httpClient.GetAsync(new Uri(AlbumCover + "?param=" +
                            StaticSource.PicSizeDownloadAlbumCover));
                        responseMessage.EnsureSuccessStatusCode();
                        using IRandomAccessStream outputStream = new InMemoryRandomAccessStream();
                        using var stream = await responseMessage.Content.ReadAsStreamAsync();
                        using var inputStream = stream.AsRandomAccessStream();
                        var decoder = await BitmapDecoder.CreateAsync(inputStream);
                        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                        var encoder =
                            await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outputStream);
                        encoder.SetSoftwareBitmap(softwareBitmap);
                        await encoder.FlushAsync();
                        using var outputReadStream = outputStream.AsStreamForRead();
                        pic = TagLibHelper.CreateCoverPicture(outputReadStream);
                    }

                    if (!string.IsNullOrWhiteSpace(_downloadAlbumId))
                        DownloadManager.CacheAlbumPicture(_downloadAlbumId, pic);

                    TagLibHelper.SetCover(file, pic);
                }

                file.Save();
            }
            catch (Exception ex)
            {
                if (_removed) return;
                _processingFailed = true;
                _ = _uiThreadDispatcher.TryRunAsync(() =>
                {
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                    Message = "写入音乐信息时出现错误" + ex.Message;
                });
                _diagnostics.AddError("写入音乐信息时出现错误" + ex.Message);
                _notification.ShowMessage("写入信息错误: " + ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        });
    }

    private Task DownloadLyric()
    {
        _ = _uiThreadDispatcher.TryRunAsync(() => Message = "下载歌词中");
        //下载歌词
        return Task.Run(async () =>
        {
            try
            {
                var lyrics = await _lyricProvider.GetLyricInfoAsync(_providerSong);
                var original = await GetLyricTextAsync(lyrics, LyricType.Original, false);
                if (string.IsNullOrWhiteSpace(original)) return;
                if (original == "[99:00.00]纯音乐，请欣赏") return;
                var lrc = Utils.ConvertPureLyric(original);
                var translation = await GetLyricTextAsync(lyrics, LyricType.Translation, false);
                if (_lyricSettings.DownloadTranslation && !string.IsNullOrWhiteSpace(translation))
                    Utils.ConvertTranslation(translation, lrc);
                var lrctxt = string.Join("\r\n", lrc.Select(t =>
                {
                    if (t.HaveTranslation && !string.IsNullOrWhiteSpace(t.Translation))
                        return "[" + t.LyricLine.StartTime.ToString(@"mm\:ss\.ff") + "]" + t.LyricLine.CurrentLyric +
                               " 「" +
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
                if (_removed) return;
                _processingFailed = true;
                _ = _uiThreadDispatcher.TryRunAsync(() =>
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

        var progress = obj.Progress;
        var now = Environment.TickCount64;
        if (progress.BytesReceived != progress.TotalBytesToReceive &&
            now - Interlocked.Read(ref _lastProgressTimestamp) < 250) return;
        if (Interlocked.Exchange(ref _progressDispatchPending, 1) != 0) return;
        Interlocked.Exchange(ref _lastProgressTimestamp, now);
        _ = PublishProgressAsync(progress.BytesReceived, progress.TotalBytesToReceive);
    }

    private async Task PublishProgressAsync(ulong received, ulong total)
    {
        try
        {
            await _uiThreadDispatcher.TryRunAsync(() =>
            {
                if (_removed || Status != DownloadStatus.Downloading) return;
                TotalSize = total;
                HadSize = received;
                Progress = (int)(received * 100 / total);
                Message = $"下载中: {GetSize(received)} / {GetSize(total)}";
            });
        }
        finally
        {
            Volatile.Write(ref _progressDispatchPending, 0);
        }
    }

    public void DownloadStartToast(string songName)
    {
        _notification.ShowMessage("下载开始", "歌曲" + songName + "下载开始");
    }

    public Task StartDownload()
    {
        if (_removed || Status != DownloadStatus.Queueing) return Task.CompletedTask;
        // Reserve the queue slot before returning to the scheduler. No provider or
        // storage code (including synchronously completed awaits) runs on the UI thread.
        Status = DownloadStatus.Downloading;
        return Task.Run(StartDownloadCoreAsync);
    }

    private async Task StartDownloadCoreAsync()
    {
        if (Interlocked.Exchange(ref _startInProgress, 1) != 0)
        {
            if (_downloadOperation?.Progress.Status == BackgroundTransferStatus.PausedByApplication)
            {
                _downloadOperation.Resume();
                await _uiThreadDispatcher.TryRunAsync(() =>
                {
                    HasPaused = false;
                    Message = "下载中";
                });
            }
            return;
        }
        try
        {
            if (_transferCompleted)
            {
                Wc_DownloadFileCompleted();
                return;
            }
            await DownloadCoreAsync();
        }
        finally
        {
            Volatile.Write(ref _startInProgress, 0);
        }
    }

    private async Task DownloadCoreAsync()
    {
        _ = _uiThreadDispatcher.TryRunAsync(() =>
        {
            HasError = false;
            HasPaused = false;
            Message = "正在预加载";
        });
        if (_removed) return;
        try
        {
            await WaitForDownloadSlotAsync();
            var fileName = _downloadSettings.DownloadFileName
                .Replace("{$SINGER}", string.Join(';', _downloadArtistNames).EscapeForPath())
                .Replace("{$SONGNAME}", SongName.EscapeForPath())
                .Replace("{$ALBUM}", AlbumName.EscapeForPath())
                .Replace("{$INDEX}",
                    (_downloadTrackId > 0 ? _downloadTrackId : _downloadOrder + 1).ToString().EscapeForPath())
                .Replace("{$CDNAME}", _downloadCdName?.EscapeForPath())
                .Replace("{$SONGID}", _downloadSongId.EscapeForPath());
            var folderName = await _downloadSettings.GetDownloadDirectoryAsync();
            var nowFolder = await StorageFolder.GetFolderFromPathAsync(folderName);
            var ses = fileName.Replace('\\', '/').Split('/');
            for (var index = 0; index < ses.Length - 1; index++)
            {
                var s = ses[index];
                folderName += "/" + s;
                nowFolder = await nowFolder.CreateFolderAsync(s, CreationCollisionOption.OpenIfExists);
            }

            _ = _uiThreadDispatcher.TryRunAsync(() =>
            {
                HasError = false;
                HasPaused = false;
                Message = "正在获取下载链接";
            });
            var qualityTags = await _qualityTagProvider.GetAvailableQualityTagsAsync(ResourceType.Audio);
            qualityTags.TryGetValue(_downloadSettings.DownloadAudioRate, out var qualityTag);
            var musicResourceProvider =
                _musicResourceProviders.FirstOrDefault(provider => provider.Id == _providerSong.ProviderId);
            if (musicResourceProvider is null)
            {
                Status = DownloadStatus.Error;
                _ = _uiThreadDispatcher.TryRunAsync(() =>
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
                _ = _uiThreadDispatcher.TryRunAsync(() =>
                {
                    Message = "获取下载链接错误";
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                });
                return;
            }

            var extension = NormalizeAudioExtension(musicResource.ExtensionName);
            var albumCover = AlbumCover ?? await GetProviderAlbumCoverAsync(_providerSong);
            fileName += "." + extension;
            // Metadata must remain available when background mode suppresses UI dispatch.
            AlbumCover = albumCover;
            await _uiThreadDispatcher.TryRunAsync(() =>
            {
                FileName = fileName;
                OnPropertyChanged(nameof(AlbumCover));
            });
            _downloadBitrate = 0;
            _downloadFormat = extension;

            var targetFileName = Path.GetFileName(fileName);
            if (await nowFolder.FileExistsAsync(targetFileName))
            {
                switch (_downloadSettings.DownloadNameOccupySolution)
                {
                    case OccupySolution.Skip:
                        Status = DownloadStatus.Paused;
                        _ = _uiThreadDispatcher.TryRunAsync(() =>
                        {
                            Message = "歌曲已存在, 跳过";
                            HasPaused = true;
                        });
                        return;
                    case OccupySolution.ReWrite:
                        await (await nowFolder.GetFileAsync(targetFileName)).DeleteAsync();
                        break;
                    case OccupySolution.AppendID:
                        fileName = Path.GetFileNameWithoutExtension(fileName) + _downloadSongId + "." + extension;
                        targetFileName = Path.GetFileName(fileName);
                        await _uiThreadDispatcher.TryRunAsync(() => FileName = fileName);
                        break;
                    case OccupySolution.UpdateInfo:
                        ResultFile = await nowFolder.GetFileAsync(targetFileName);
                        FullPath = ResultFile.Path;
                        Wc_DownloadFileCompleted();
                        return;
                }
            }

            await WaitForDownloadSlotAsync();
            var targetFile = await nowFolder.CreateFileAsync(targetFileName);
            if (_removed)
            {
                await targetFile.DeleteAsync();
                return;
            }
            _downloadOperation = DownloadManager.Downloader.CreateDownload(
                musicResource.Uri,
                targetFile
            );
            FullPath = _downloadOperation.ResultFile.Path;
            if (_removed)
            {
                _downloadOperation = null;
                await targetFile.DeleteAsync();
                return;
            }
            //_downloadOperation.IsRandomAccessRequired = true;
            var process = new Progress<DownloadOperation>(Wc_DownloadProgressChanged);
            //DownloadStartToast(FileName);
            await WaitForDownloadSlotAsync();
            await _downloadOperation.StartAsync().AsTask(_downloadCancellation.Token, process);
            Wc_DownloadFileCompleted();
        }
        catch (Exception ex)
        {
            if (_removed) return;
            Status = DownloadStatus.Error;
            _downloadOperation = null;
            _ = _uiThreadDispatcher.TryRunAsync(() =>
            {
                Message = "下载错误: " + ex.Message;
                HasError = true;
                HasPaused = true;
            });
            _diagnostics.AddError("无法下载歌曲 " + SongName + "\n已自动将其从下载列表中移除" + ex.Message);
        }
    }

    private async Task WaitForDownloadSlotAsync()
    {
        // Pausing during URL/file preparation must keep the owning worker alive.
        // Resume queues the item; only the scheduler grants a slot and wakes it.
        while (Status is DownloadStatus.Paused or DownloadStatus.Queueing)
            await Task.Delay(100, _downloadCancellation.Token);
        _downloadCancellation.Token.ThrowIfCancellationRequested();
    }

    private static string[] GetProviderArtistNames(SingleSongBase song)
    {
        return song.CreatorList?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? [];
    }

    private static string NormalizeAudioExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new InvalidDataException("下载 API 未返回音频文件格式");

        var normalized = extension.Trim().TrimStart('.').ToLowerInvariant();
        return normalized switch
        {
            "mp3" or "flac" or "ape" or "m4a" or "wav" or "aac" => normalized,
            _ => throw new InvalidDataException($"下载 API 返回了不支持的音频文件格式: {extension}")
        };
    }

    private static async Task<string?> GetProviderAlbumCoverAsync(SingleSongBase song)
    {
        var coverProvider = song.Album as IHasCover ?? song as IHasCover;
        if (coverProvider is null)
            return null;

        var result = await coverProvider.GetCoverAsync();
        return result is IResourceResultOf<Uri?> uriResult
            ? (await uriResult.GetResourceAsync())?.ToString()
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

    [GeneratedRegex("[0-9]+")]
    private static partial Regex DiscInfoRegex();
}

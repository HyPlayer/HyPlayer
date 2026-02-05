#region

using HyPlayer.Classes;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using TagLib;
using Windows.Graphics.Imaging;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using File = TagLib.File;

#endregion

namespace HyPlayer.HyPlayControl;

internal sealed class DownloadObject : INotifyPropertyChanged
{
    public PlayItem DontUsePlayItem;
    private DownloadOperation _downloadOperation;

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

    public ulong HadSize
    {
        get => _hadSize;
        set => SetField(ref _hadSize, value);
    }

    public NCSong ncsong;

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

    public DownloadObject(NCSong song)
    {
        ncsong = song;
    }

    public void Pause()
    {
        if (_downloadOperation is { Progress.Status: BackgroundTransferStatus.Running })
            _downloadOperation?.Pause();
        Status = DownloadStatus.Paused;
        _ = Common.Invoke(() =>
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
        _ = Common.Invoke(() =>
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
        _ = Common.Invoke(() =>
        {
            Message = "已移除";
            HasPaused = false;
        });
    }

    private void Wc_DownloadFileCompleted()
    {
        DownloadManager.WritingTasks.Add(Task.Run(async () =>
        {
            if (Common.Setting.downloadLyric)
                await DownloadLyric().ConfigureAwait(false);
            if (Common.Setting.writedownloadFileInfo)
                await WriteInfoToFile().ConfigureAwait(false);
            DownloadManager.WritingTasks.RemoveAll(t => t.IsCompleted);
            Status = DownloadStatus.Finished;
        }));
        _ = Common.Invoke(() => Message = "下载完成");
    }

    private Task WriteInfoToFile()
    {
        _ = Common.Invoke(() => Message = "正在写文件信息");
        return Task.Run(async () =>
        {
            using var streamAbstraction = new UwpStorageFileAbstraction(ResultFile);
            using var file = File.Create(streamAbstraction);
            try
            {
                if (Common.Setting.write163Info && DontUsePlayItem is not null)
                    The163KeyHelper.TrySetMusicInfo(file.Tag, DontUsePlayItem);
                //写相关信息
                file.Tag.Album = ncsong.Album.Name;
                file.Tag.Performers = ncsong.Artist.Select(t => t.Name).ToArray();
                file.Tag.Title = ncsong.SongName;
                file.Tag.Track = (uint)(ncsong.TrackId == -1 ? ncsong.Order + 1 : ncsong.TrackId);

                // 获取 Disc Id
                var regexRet = Regex.Match(ncsong.CDName ?? "01", "[0-9]+");
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
                using var responseMessage = await Common.HttpClient.GetAsync(new Uri(ncsong.Album.Cover + "?param=" +
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

                file.Tag.Pictures = new IPicture[]
                {
                    pic
                };
                file.Tag.Pictures[0].MimeType = "image/jpeg";
                file.Tag.Pictures[0].Description = "Cover.jpg";
                file.Save();
            }
            catch (Exception ex)
            {
                Status = DownloadStatus.Error;
                _ = Common.Invoke(() =>
                {
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                    Message = "写入音乐信息时出现错误" + ex.Message;
                });
                Common.ErrorMessageList.Add("写入音乐信息时出现错误" + ex.Message);
                Common.AddToTeachingTipLists("写入信息错误: " + ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        });
    }

    private Task DownloadLyric()
    {
        _ = Common.Invoke(() => Message = "下载歌词中");
        //下载歌词
        return Task.Run(async () =>
        {
            var lyricRequest = new LyricRequest() { Id = ncsong.SongId };
            var lyricResult = await Common.NeteaseAPI.RequestAsync(NeteaseApis.LyricApi, lyricRequest);
            if (lyricResult.IsSuccess)
            {
                var data = lyricResult.Value;
                if (data.Lyric == null) return;
                if (data.Lyric.Lyric == "[99:00.00]纯音乐，请欣赏") return;
                var lrc = Utils.ConvertPureLyric(data.Lyric.Lyric);
                if (Common.Setting.downloadTranslation && data.TranslationLyric != null)
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
                _ = Common.Invoke(() =>
                {
                    Message = "下载歌词错误: " + lyricResult.Error.Message;
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                });
                Common.AddToTeachingTipLists("下载歌词错误: " + lyricResult.Error.Message);
            }
        });
    }

    private static string GetSize(double size)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
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

        _ = Common.Invoke((() =>
        {
            TotalSize = obj.Progress.TotalBytesToReceive;
            HadSize = obj.Progress.BytesReceived;
            Progress = (int)(obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive);
            Message = $"下载中: {GetSize(obj.Progress.BytesReceived)} / {GetSize(obj.Progress.TotalBytesToReceive)}";
        }));

        if (HadSize == TotalSize && Status == DownloadStatus.Finished) return;
    }

    public static void DownloadStartToast(string SongName)
    {
        Common.AddToTeachingTipLists("下载开始", "歌曲" + SongName + "下载开始");
    }

    public async Task StartDownload()
    {
        if (_downloadOperation != null) { Resume(); return; }
        Status = DownloadStatus.Downloading;
        _ = Common.Invoke(() =>
        {
            HasError = false;
            HasPaused = false;
            Message = "正在预加载";
        });
        try
        {
            FileName = Common.Setting.downloadFileName
                .Replace("{$SINGER}", string.Join(';', ncsong.Artist.Select(t => t.Name)).EscapeForPath())
                .Replace("{$SONGNAME}", ncsong.SongName.EscapeForPath())
                .Replace("{$ALBUM}", ncsong.Album.Name.EscapeForPath())
                .Replace("{$INDEX}",
                    (ncsong.GetType() == typeof(NCAlbumSong) ? ncsong.Order : ncsong.Order + 1).ToString().EscapeForPath())
                .Replace("{$CDNAME}", ncsong.CDName?.EscapeForPath())
                .Replace("{$SONGID}", ncsong.SongId?.EscapeForPath());
            var folderName = Common.Setting.downloadDir;
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
                switch (Common.Setting.downloadNameOccupySolution)
                {
                    case 0:
                        Status = DownloadStatus.Paused;
                        _ = Common.Invoke(() => { Message = "歌曲已存在, 跳过"; });
                        return;
                    case 1:
                        await (await nowFolder.GetFileAsync(Path.GetFileName(FileName))).DeleteAsync();
                        break;
                    case 2:
                        FileName = Path.GetFileNameWithoutExtension(FileName) + ncsong.SongId;
                        break;
                    case 3:
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
            _ = Common.Invoke(() =>
            {
                HasError = false;
                HasPaused = false;
                Message = "正在获取下载链接";
            });
            var urlRequest = new SongUrlRequest() { Id = ncsong.SongId, Level = Common.Setting.downloadAudioRate };
            var urlResult = await Common.NeteaseAPI.RequestAsync(NeteaseApis.SongUrlApi, urlRequest);

            if (urlResult.IsError || urlResult.Value?.SongUrls?[0] is null)
            {
                Status = DownloadStatus.Error;
                _ = Common.Invoke(() =>
                {
                    Message = "获取下载链接错误";
                    HasError = true;
                    HasPaused = true;
                    Progress = 100;
                });
                return;
            }

            if (urlResult.Value.SongUrls[0].FreeTrialInfo is not null && Common.Setting.jumpVipSongDownloading)
            {
                Status = DownloadStatus.Paused;
                _ = Common.Invoke(() =>
                {
                    HasPaused = true;
                    Progress = 100;
                    Message = "VIP 试听歌曲, 跳过";
                });
                return;
            }

            FileName += "." + urlResult.Value.SongUrls[0].Type?.ToLowerInvariant();
            DontUsePlayItem = new PlayItem
            {
                Bitrate = Convert.ToInt32(urlResult.Value.SongUrls[0].BitRate),
                QualityTag = "下载",
                InfoTag = "下载",
                Album = ncsong.Album,
                Translation = ncsong.TranslatedName,
                Artist = ncsong.Artist,
                SubExt = urlResult.Value.SongUrls[0].Type.ToLowerInvariant(),
                Id = ncsong.SongId,
                Name = ncsong.SongName,
                Type = HyPlayItemType.Netease,
                TrackId = ncsong.TrackId,
                CDName = ncsong.CDName,
                Url = urlResult.Value.SongUrls[0].Url,
                LengthInMilliseconds = ncsong.LengthInMilliseconds,
                Size = urlResult.Value.SongUrls[0].Size,
                //md5 = json["data"][0]["md5"].ToString()
            };

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
            _ = Common.Invoke(() => { Message = "下载错误: " + ex.Message; });
            Common.ErrorMessageList.Add("无法下载歌曲 " + ncsong.SongName + "\n已自动将其从下载列表中移除" + ex.Message);
        }
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
}

internal static class DownloadManager
{
    private static readonly Timer _timer = new(1000);
    private static bool Timered;
    public static ObservableCollection<DownloadObject> DownloadLists = new();
    public static BackgroundDownloader Downloader = new();
    public static List<Task> WritingTasks = new();
    public static Dictionary<string, Picture> AlbumPicturesCache = new();

    public static bool CheckDownloadAbilityAndToast()
    {
        Common.AddToTeachingTipLists("开始下载");
        return true;
    }

    public static void AddDownload(NCSong song)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        if (!Timered)
        {
            _timer.Elapsed += Timer_Elapsed;
            _timer.Start();
            Timered = true;
        }

        DownloadLists.Add(new DownloadObject(song));
    }

    private static void Timer_Elapsed(object sender, ElapsedEventArgs elapsedEventArgs)
    {
        if (DownloadLists.Count == 0) return;
        var maxDownloadCount = Common.Setting.maxDownloadCount;
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
                    _ = Common.Invoke(() => { DownloadLists.RemoveAt(i1); });
                    break;
                case DownloadObject.DownloadStatus.Paused:
                case DownloadObject.DownloadStatus.Error:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public static void AddDownload(List<NCSong> songs)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        if (!Timered)
        {
            _timer.Elapsed += Timer_Elapsed;
            _timer.Start();
            Timered = true;
        }

        songs.ForEach(t => { DownloadLists.Add(new DownloadObject(t)); });
    }
}

public partial class UwpStorageFileAbstraction : File.IFileAbstraction, IDisposable
{
    private readonly IStorageFile file;
    private bool disposedValue;

    public UwpStorageFileAbstraction(IStorageFile file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        this.file = file;
        Name = file.Name;
        ReadStream = file.OpenStreamForReadAsync().GetAwaiter().GetResult();
        WriteStream = file.OpenStreamForWriteAsync().GetAwaiter().GetResult();
    }

    public UwpStorageFileAbstraction(Stream readStream, Stream writeStream, string name = "HyPlayer Music")
    {
        ReadStream = readStream;
        WriteStream = writeStream;
        Name = name;
    }


    public string Name { get; }

    public Stream ReadStream { get; }

    public Stream WriteStream { get; }

    public void CloseStream(Stream stream)
    {
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                ReadStream?.Dispose();
                WriteStream?.Dispose();
            }
            disposedValue = true;
        }
    }
    ~UwpStorageFileAbstraction()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
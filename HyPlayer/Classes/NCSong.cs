#region

using ALRC.Abstraction;
using HyPlayer.Classes.LyricParser.Abstraction;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.NeteaseApi.Models;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.ServiceModel.Channels;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TagLib;
using Windows.Graphics.Imaging;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Microsoft.Toolkit.Uwp.Helpers;
using File = TagLib.File;

#endregion

namespace HyPlayer.Classes;

/// <summary>
/// 等价于HyPlayer.PlayCore的同名类. 
/// </summary>
public class ProvidableItemBase
{

}

/// <summary>
/// 资源类型ID定义类.（啊没错，又是从NeteaseProvider里抄来的）
/// </summary>
public static class NeteaseTypeIds
{
    /// <summary>
    /// 单曲
    /// </summary>
    public const string SingleSong = "sg";

    /// <summary>
    /// 歌单
    /// </summary>
    public const string Playlist = "pl";

    /// <summary>
    /// 专辑
    /// </summary>
    public const string Artist = "ar";

    /// <summary>
    /// 歌手
    /// </summary>
    public const string Album = "al";

    /// <summary>
    /// 用户
    /// </summary>
    public const string User = "us";

    /// <summary>
    /// 电台节目
    /// </summary>
    public const string RadioProgram = "pr";

    /// <summary>
    /// 电台播客
    /// </summary>
    public const string RadioChannel = "dj";

    /// <summary>
    /// MV
    /// </summary>
    public const string Mv = "mv";

    /// <summary>
    /// MBlog
    /// </summary>
    public const string MBlog = "mb";

    /// <summary>
    /// 搜索结果
    /// </summary>
    public const string SearchResult = "sr";

    /// <summary>
    /// 方法歌曲容器
    /// </summary>
    public const string ActionGettableSongContainer = "ag";

    /// <summary>
    /// 排行榜
    /// </summary>
    public const string Chart = "ct";

    /// <summary>
    /// 动态
    /// </summary>
    public const string Dynamic = "dy";

    /// <summary>
    /// 歌单分类
    /// </summary>
    public const string PlaylistCategory = "PC";

    /// <summary>
    /// 歌词
    /// </summary>
    public const string Lyric = "lr";

    /// <summary>
    /// 私人 FM
    /// </summary>
    public const string PersonalFm = "fm";
}


public class HyPlayItem
{
    public HyPlayItemType ItemType;
    public PlayItem PlayItem;

    public NCSong ToNCSong()
    {
        if (PlayItem != null)
            return PlayItem.ToNCSong();
        return new NCSong();
    }
}

public class HyPlayerItemComparer : IEqualityComparer<HyPlayItem>
{
    public bool Equals(HyPlayItem x, HyPlayItem y)
    {
        return x?.ToNCSong().sid == y?.ToNCSong().sid;
    }

    public int GetHashCode(HyPlayItem obj)
    {
        return obj.ToNCSong().sid.GetHashCode();
    }
}

public enum HyPlayItemType
{
    Local,
    LocalProgressive,
    Netease,
    Radio
}

public class KaraokLyricInfo : PureLyricInfo
{
    public string KaraokLyric { get; set; }
    public string YrTrLyrics { get; set; }
    public string YrNeteaseRomaji { get; set; }
}

public class ALRCLyricInfo : PureLyricInfo
{
    public ALRCFile ALRC { get; set; }
}

[JsonDerivedType(typeof(ALRCLyricInfo), "ALRC")]
[JsonDerivedType(typeof(KaraokLyricInfo), "Karaok")]
public class PureLyricInfo
{
    public string PureLyrics { get; set; }
    public string TrLyrics { get; set; }
    public string NeteaseRomaji { get; set; }
    public List<LyricInfoMetadata> SongMetadata { get; set; } = [];
    public List<LyricInfoMetadata> LyricMetadata { get; set; } = [];
}

public class SongLyric
{
    public static SongLyric PureSong = new()
    { LyricLine = new LrcLyricsLine("纯音乐 请欣赏", TimeSpan.Zero) };

    public static SongLyric NoLyric = new()
    { LyricLine = new LrcLyricsLine("无歌词 请欣赏", TimeSpan.Zero) };

    public static SongLyric LoadingLyric = new()
    { LyricLine = new LrcLyricsLine("加载歌词中...", TimeSpan.Zero) };

    public LyricLine LyricLine { get; set; }
    public string Translation { get; set; }
    public string Romaji { get; set; }

    public bool HaveTranslation => !string.IsNullOrEmpty(Translation);
    public bool HaveRomaji => !string.IsNullOrEmpty(Romaji);
}

public class NCRadio : ProvidableItemBase
{
    public string cover;
    public string desc;
    public NCUser DJ;
    public string id;
    public string lastProgramName;
    public string name;
    public bool subed;
}

public class NCFmItem : NCSong
{
    public string description;
    public string fmId;
    public string RadioId;
    public string RadioName;
}

public class NCSong : ProvidableItemBase
{
    public NCAlbum Album;
    public string alias;
    public List<NCArtist> Artist;
    public string CDName;
    public bool IsAvailable = true;
    public bool IsCloud;
    public bool IsVip;

    public double LengthInMilliseconds;

    public string mvid;
    public int Order = 0;
    public string sid;
    public string songname;
    public int TrackId = -1;
    public string transname;
    public HyPlayItemType Type;
    public int DspOrder => Order + 1;


    public Uri Cover =>
        Common.Setting.noImage
            ? null
            : new Uri((Album.cover ??
                       "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg") +
                      "?param=" +
                      StaticSource.PICSIZE_SINGLENCSONG_COVER);
    public string CoverString =>
        Common.Setting.noImage
            ? null
            : new Uri((Album.cover ??
                       "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg") +
                      "?param=" +
                      StaticSource.PICSIZE_HOME_CARD_COVER).ToString();

    public string ArtistString
    {
        get { return string.Join(" / ", Artist.Select(t => t.name)); }
    }


    public string ConvertTranslate(string source)
    {
        return string.IsNullOrEmpty(source) ? "" : "(" + source + ")";
    }
}

public class NCAlbumSong : NCSong
{
    public string DiscName { get; set; }
}

public class SimpleListItem
{
    public bool CanPlay;
    public string CoverLink;
    public string LineOne;
    public string LineThree;
    public string LineTwo;
    public int Order = 0;
    public string ResourceId;
    public string Title;

    public Uri CoverUri =>
        Common.Setting.noImage
            ? null
            : new Uri((string.IsNullOrEmpty(CoverLink)
                          ? "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg"
                          : CoverLink) +
                      "?param=" +
                      StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM);

    public int DspOrder => Order + 1;
}

public class PlayItem
{
    public NCAlbum Album;
    public List<NCArtist> Artist;
    public int Bitrate;
    public string CDName;
    public string Translation;
    public Tag LocalFileTag;
    public StorageFile DontSetLocalStorageFile; //如非特殊原因请不要设置这个东西!
    public string Id;
    public bool IsLocalFile;
    public double LengthInMilliseconds;
    public string Name;
    public string Size;
    public string SubExt;
    public string QualityTag;
    public string InfoTag;
    public int TrackId;
    public HyPlayItemType Type;
    public string Url;
    public double Volume = 1d;
    public AudioGraphPlaybackSource AudioGraphPlaybackSource;
    public InMemoryRandomAccessStream NcmPlayableStream;
    public string NcmPlayableStreamMIMEType = string.Empty;

    public string ArtistString
    {
        get { return string.Join(" / ", Artist.Select(t => t.name)); }
    }

    public string AlbumString => Album.name ?? "未知专辑";

    public NCSong ToNCSong()
    {
        return new NCSong
        {
            Type = Type,
            Album = Album,
            Artist = Artist,
            LengthInMilliseconds = LengthInMilliseconds,
            sid = Id,
            songname = Name,
            TrackId = TrackId
        };
    }
    public void FreePlaybackResources()
    {
        AudioGraphPlaybackSource?.Dispose();
        NcmPlayableStream?.Dispose();
        NcmPlayableStreamMIMEType = null;
        AudioGraphPlaybackSource = null;
        NcmPlayableStream = null;
    }
}

public class NCPlayList : ProvidableItemBase
{
    public long bookCount;
    public string cover;
    public NCUser creater;
    public string desc;
    public string name;
    public long playCount;
    public string plid;
    public bool subscribed;
    public long trackCount;
    public DateTime createTime;
    public DateTime updateTime;

}

public class NCUser : ProvidableItemBase
{
    public string avatar;
    public string id;
    public string name;
    public string signature;
}

public class NCMlog : ProvidableItemBase
{
    public string cover;
    public string description;
    public int duration;
    public string id;
    public string title;
}

public class NCArtist : ProvidableItemBase
{
    public string alias;
    public string avatar;
    public string id;
    public string name;
    public string transname;
    public HyPlayItemType Type;

    public static NCArtist CreateFromJson(JToken artist)
    {
        //TODO: 歌手这里尽量再来点信息
        var art = new NCArtist
        {
            Type = HyPlayItemType.Netease,
            id = artist["id"].ToString(),
            name = artist["name"].ToString()
        };
        if (artist["alias"] != null)
            art.alias = string.Join(" / ", artist["alias"].Select(t => t.ToString()).ToArray());
        if (artist["trans"] != null) art.transname = artist["trans"].ToString();
        if (artist["picUrl"] != null) art.avatar = artist["picUrl"].ToString();
        return art;
    }
}

public class NCAlbum : ProvidableItemBase
{
    public HyPlayItemType AlbumType;
    public string alias;
    public string cover;
    public string description;
    public string id;
    public string name;

    public static NCAlbum CreateFromJson(JToken album)
    {
        if (album?.HasValues is not true) return new NCAlbum();
        return new NCAlbum
        {
            AlbumType = HyPlayItemType.Netease,
            alias = album["alias"] != null
                ? string.Join(" / ", album["alias"].ToArray().Select(t => t.ToString()))
                : "",
            cover = album["picUrl"].ToString(),
            description = album["description"] != null ? album["description"].ToString() : "",
            id = album["id"].ToString(),
            name = album["name"].ToString()
        };
    }
}

public class Comment : ProvidableItemBase
{
    public Comment thisComment => this; //绑定回去用
    public string cid;
    public string content;
    public bool HasLiked;
    public bool IsMainComment = true;
    public int likedCount;
    public int ReplyCount;
    public string resourceId;
    public NeteaseResourceType resourceType;
    public DateTime SendTime;
    public NCUser CommentUser;
    public bool IsByMyself => CommentUser.id == Common.LoginedUser?.id;
}

public sealed class DownloadTask : INotifyPropertyChanged
{
    #region Internal Methods and Properties
    private DownloadOperation _downloadOperation;
    private IStorageFile _resultFileBackingField;
    private ulong _totalSize;
    private int _progress;
    private ulong _hadSize;
    private string _fileName;
    private string _message;
    private bool _hasError;
    private bool _hasPaused;

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
            var file = TagLib.File.Create(streamAbstraction);
            try
            {
                if (Common.Setting.write163Info && PlayItem is not null)
                    The163KeyHelper.TrySetMusicInfo(file.Tag, PlayItem);
                //写相关信息
                file.Tag.Album = ncsong.Album.name;
                file.Tag.Performers = ncsong.Artist.Select(t => t.name).ToArray();
                file.Tag.Title = ncsong.songname;
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
                using var responseMessage = await Common.HttpClient.GetAsync(new Uri(ncsong.Album.cover + "?param=" +
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
                DownloadManager.AlbumPicturesCache[ncsong.Album.id] = pic;

                file.Tag.Pictures = new IPicture[]
                {
                    pic
                };
                file.Tag.Pictures[0].MimeType = "image/jpeg";
                file.Tag.Pictures[0].Description = "cover.jpg";
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
            finally
            {
                file.Save();
                file.Dispose();
                streamAbstraction.Dispose();
            }
        });
    }

    private Task DownloadLyric()
    {
        _ = Common.Invoke(() => Message = "下载歌词中");
        //下载歌词
        return Task.Run(async () =>
        {
            var lyricRequest = new LyricRequest() { Id = ncsong.sid };
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



    #endregion

    #region Public Properties
    public event PropertyChangedEventHandler PropertyChanged;

    public PlayItem PlayItem { get; set; }

    public ulong TotalSize
    {
        get => _totalSize;
        set => SetField(ref _totalSize, value);
    }

    public IStorageFile ResultFile
    {
        get => _resultFileBackingField;
        set
        {
            _resultFileBackingField = value;
            OnPropertyChanged(nameof(ResultFile));
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
    #endregion

    public DownloadTask(NCSong song)
    {
        ncsong = song;
    }

    #region Public Methods

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

    
    public static void DownloadStartToast(string songname)
    {
        Common.AddToTeachingTipLists("下载开始", "歌曲" + songname + "下载开始");
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
                .Replace("{$SINGER}", string.Join(';', ncsong.Artist.Select(t => t.name)).EscapeForPath())
                .Replace("{$SONGNAME}", ncsong.songname.EscapeForPath())
                .Replace("{$ALBUM}", ncsong.Album.name.EscapeForPath())
                .Replace("{$INDEX}",
                    (ncsong.GetType() == typeof(NCAlbumSong) ? ncsong.Order : ncsong.Order + 1).ToString().EscapeForPath())
                .Replace("{$CDNAME}", ncsong.CDName?.EscapeForPath())
                .Replace("{$SONGID}", ncsong.sid?.EscapeForPath());
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
                        FileName = Path.GetFileNameWithoutExtension(FileName) + ncsong.sid;
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
            var urlRequest = new SongUrlRequest() { Id = ncsong.sid, Level = Common.Setting.downloadAudioRate };
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
            PlayItem = new PlayItem
            {
                Bitrate = Convert.ToInt32(urlResult.Value.SongUrls[0].BitRate),
                QualityTag = "下载",
                InfoTag = "下载",
                Album = ncsong.Album,
                Translation = ncsong.transname,
                Artist = ncsong.Artist,
                SubExt = urlResult.Value.SongUrls[0].Type.ToLowerInvariant(),
                Id = ncsong.sid,
                Name = ncsong.songname,
                Type = HyPlayItemType.Netease,
                TrackId = ncsong.TrackId,
                CDName = ncsong.CDName,
                Url = urlResult.Value.SongUrls[0].Url,
                LengthInMilliseconds = ncsong.LengthInMilliseconds,
                Size = urlResult.Value.SongUrls[0].Size.ToString(),
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
            Common.ErrorMessageList.Add("无法下载歌曲 " + ncsong.songname + "\n已自动将其从下载列表中移除" + ex.Message);
        }
    }
    #endregion

}

public class UwpStorageFileAbstraction : File.IFileAbstraction, IDisposable
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

public enum DownloadStatus
{
    Queueing,
    Downloading,
    Finished,
    Paused,
    Error
}
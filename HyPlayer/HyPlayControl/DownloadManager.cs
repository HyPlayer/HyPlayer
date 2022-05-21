#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using HyPlayer.Classes;
using Microsoft.Toolkit.Uwp.Notifications;
using NeteaseCloudMusicApi;
using TagLib;
using File = TagLib.File;

#endregion

namespace HyPlayer.HyPlayControl;

internal class DownloadObject
{
    private PlayItem dontuseme;
    public DownloadOperation downloadOperation;

    public string fullpath;
    public ulong HavedSize;
    public NCSong ncsong;

    public int progress;

    // 0 - 排队 1 - 下载中 2 - 下载完成  3 - 暂停
    public int Status;
    public ulong TotalSize;

    public DownloadObject(NCSong song)
    {
        ncsong = song;
    }

    public string filename;

    private void Wc_DownloadFileCompleted()
    {
        Status = 2;
        if (Common.Setting.downloadLyric)
            DownloadLyric();
        if (Common.Setting.writedownloadFileInfo)
            WriteInfoToFile();
        /*
        try
        {
            var downloadToastContent = new ToastContent
            {
                Visual = new ToastVisual
                {
                    BindingGeneric = new ToastBindingGeneric
                    {
                        Children =
                        {
                            new AdaptiveText
                            {
                                Text = "下载完成",
                                HintStyle = AdaptiveTextStyle.Header
                            },
                            new AdaptiveText
                            {
                                Text = filename
                            }
                        }
                    }
                },
                Launch = "",
                Scenario = ToastScenario.Reminder,
                Audio = new ToastAudio { Silent = true }
            };
            var toast = new ToastNotification(downloadToastContent.GetXml())
            {
                Tag = "HyPlayerDownloadDone",
                Data = new NotificationData()
            };
            toast.Data.SequenceNumber = 0;
            var notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(toast);
            
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
        */
        Common.AddToTeachingTipLists(filename + "下载完成");
    }

    private async void WriteInfoToFile()
    {
        var file = File.Create(
            new UwpStorageFileAbstraction(await StorageFile.GetFileFromPathAsync(fullpath)));
        if (Common.Setting.write163Info)
            The163KeyHelper.TrySetMusicInfo(file.Tag, dontuseme);
            //写相关信息
            file.Tag.Album = ncsong.Album.name;
            file.Tag.Performers = ncsong.Artist.Select(t => t.name).ToArray();
            file.Tag.Title = ncsong.songname;
            file.Tag.Pictures = new IPicture[]
            {
                new Picture(ByteVector.FromStream(RandomAccessStreamReference
                    .CreateFromUri(new Uri(ncsong.Album.cover + "?param=" +
                                           StaticSource.PICSIZE_DOWNLOAD_ALBUMCOVER))
                    .OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead()))
            };
            file.Tag.Pictures[0].MimeType = "image/jpeg";
            file.Tag.Pictures[0].Description = "cover.jpg";
            file.Save();
    }

    private async void DownloadLyric()
    {
        //下载歌词
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Lyric,
                new Dictionary<string, object> { { "id", ncsong.sid } });
            if (!(json.ContainsKey("nolyric") && json["nolyric"].ToString().ToLower() == "true") &&
                !(json.ContainsKey("uncollected") && json["uncollected"].ToString().ToLower() == "true"))
            {
                var lrc = Utils.ConvertPureLyric(json["lrc"]["lyric"].ToString());
                if (Common.Setting.downloadTranslation && json["tlyric"]?["lyric"] != null)
                    Utils.ConvertTranslation(json["tlyric"]["lyric"].ToString(), lrc);
                var lrctxt = string.Join("\r\n", lrc.Select(t =>
                {
                    if (t.HaveTranslation && !string.IsNullOrWhiteSpace(t.Translation))
                        return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric + " 「" +
                               t.Translation + "」";
                    return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric;
                }));
                var sf = await (await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(fullpath)))
                    .CreateFileAsync(
                        Path.GetFileName(Path.ChangeExtension(fullpath, "lrc")),
                        CreationCollisionOption.ReplaceExisting);
                if (Common.Setting.usingGBK)
                {
                    await FileIO.WriteBytesAsync(sf,
                        Encoding.Convert(Encoding.UTF8, Encoding.GetEncoding("GBK"), Encoding.UTF8.GetBytes(lrctxt)));
                }
                else
                {
                    await FileIO.WriteTextAsync(sf, lrctxt);
                }
            }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void Wc_DownloadProgressChanged(DownloadOperation obj)
    {
        if (obj.Progress.TotalBytesToReceive == 0)
        {
            Status = 0;
            downloadOperation = null;
            return;
        }

        TotalSize = obj.Progress.TotalBytesToReceive;
        HavedSize = obj.Progress.BytesReceived;
        progress = (int)(obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive);
        if (TotalSize == HavedSize)
            Wc_DownloadFileCompleted();
    }

    public static void DownloadStartToast(string songname)
    {
        /*
        var downloadToastContent = new ToastContent
        {
            Visual = new ToastVisual
            {
                BindingGeneric = new ToastBindingGeneric
                {
                    Children =
                    {
                        new AdaptiveText
                        {
                            Text = "下载开始",
                            HintStyle = AdaptiveTextStyle.Header
                        },
                        new AdaptiveText
                        {
                            Text = songname
                        }
                    }
                }
            },
            Launch = "",
            Scenario = ToastScenario.Reminder,
            Audio = new ToastAudio { Silent = true }
        };
        var toast = new ToastNotification(downloadToastContent.GetXml())
        {
            Tag = "HyPlayerDownloadStart",
            Data = new NotificationData()
        };

        toast.Data.SequenceNumber = 0;
        var notifier = ToastNotificationManager.CreateToastNotifier();
        notifier.Show(toast);
        */
        Common.AddToTeachingTipLists("下载开始", "歌曲" + songname + "下载开始");
    }

    public async void StartDownload()
    {
        if (downloadOperation != null || Status == 1) return;
        Status = 1;
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                new Dictionary<string, object> { { "id", ncsong.sid }, { "br", Common.Setting.downloadAudioRate } });

            if (json["data"][0]["code"].ToString() != "200")
            {
                Common.AddToTeachingTipLists("无法下载", "无法下载歌曲 " + ncsong.songname + "\n已自动将其从下载列表中移除");
                DownloadManager.DownloadLists.Remove(DownloadManager.DownloadLists.FirstOrDefault());
                return; //未获取到
            }

            dontuseme = new PlayItem
            {
                Bitrate = json["data"][0]["br"].ToObject<int>(),
                Tag = "下载",
                Album = ncsong.Album,
                Artist = ncsong.Artist,
                SubExt = json["data"][0]["type"].ToString().ToLowerInvariant(),
                Id = ncsong.sid,
                Name = ncsong.songname,
                Type = HyPlayItemType.Netease,
                Url = json["data"][0]["url"].ToString(),
                LengthInMilliseconds = ncsong.LengthInMilliseconds,
                Size = json["data"][0]["size"].ToString()
                //md5 = json["data"][0]["md5"].ToString()
            };
            filename = Common.Setting.downloadFileName
                           .Replace("{$SINGER}", string.Join(';', ncsong.Artist.Select(t => t.name)).EscapeForPath())
                           .Replace("{$SONGNAME}", ncsong.songname.EscapeForPath())
                           .Replace("{$ALBUM}", ncsong.Album.name.EscapeForPath())
                           .Replace("{$INDEX}", (ncsong.Order + 1).ToString().EscapeForPath())
                       + "." +
                       json["data"][0]["type"].ToString().ToLowerInvariant();
            string folderName = Common.Setting.downloadDir;
            var nowFolder = await StorageFolder.GetFolderFromPathAsync(folderName);
            var ses = filename.Replace('\\', '/').Split('/');
            for (var index = 0; index < ses.Length - 1; index++)
            {
                var s = ses[index];
                folderName += "/" + s;
                nowFolder = await nowFolder.CreateFolderAsync(s, CreationCollisionOption.OpenIfExists);
            }

            downloadOperation = DownloadManager.Downloader.CreateDownload(
                new Uri(json["data"][0]["url"].ToString()),
                await nowFolder.CreateFileAsync(Path.GetFileName(filename))
            );
            fullpath = downloadOperation.ResultFile.Path;
            var process = new Progress<DownloadOperation>(Wc_DownloadProgressChanged);
            _ = downloadOperation.StartAsync().AsTask(process);
            DownloadStartToast(filename);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("无法下载歌曲 " + ncsong.songname + "\n已自动将其从下载列表中移除", ex.Message);
            DownloadManager.DownloadLists.Remove(DownloadManager.DownloadLists.FirstOrDefault());
        }
    }
}

internal static class DownloadManager
{
    private static readonly bool Timered = false;
    public static List<DownloadObject> DownloadLists = new();
    public static BackgroundDownloader Downloader = new();

    public static bool CheckDownloadAbilityAndToast()
    {
        return true;
    }

    public static void AddDownload(NCSong song)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        if (!Timered)
            HyPlayList.OnTimerTicked += Timer_Elapsed;
        DownloadLists.Add(new DownloadObject(song));
    }

    private static void Timer_Elapsed()
    {
        if (DownloadLists.Count == 0) return;
        if (DownloadLists[0].Status == 1) return;
        if (DownloadLists[0].Status == 3)
            for (var i = 0; i < DownloadLists.Count; i++)
            {
                if (DownloadLists[i].Status == 2) DownloadLists.RemoveAt(i);
                if (DownloadLists[i].Status == 1) return;
                if (DownloadLists[i].Status == 0)
                {
                    DownloadLists[i].StartDownload();
                    return;
                }
            }

        if (DownloadLists[0].Status == 2)
        {
            DownloadLists.RemoveAt(0);
            if (DownloadLists.Count != 0) return;
            /*
            var downloadToastContent = new ToastContent
            {
                Visual = new ToastVisual
                {
                    BindingGeneric = new ToastBindingGeneric
                    {
                        Children =
                        {
                            new AdaptiveText
                            {
                                Text = "下载全部完成",
                                HintStyle = AdaptiveTextStyle.Header
                            }
                        }
                    }
                },
                Launch = "",
                Scenario = ToastScenario.Reminder,
                Audio = new ToastAudio { Silent = true }
            };
            var toast = new ToastNotification(downloadToastContent.GetXml())
            {
                Tag = "HyPlayerDownloadAllDone",
                Data = new NotificationData()
            };
            toast.Data.SequenceNumber = 0;
            var notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(toast);
            */
            Common.AddToTeachingTipLists("下载全部完成");

            return;
        }

        if (DownloadLists[0].Status == 0) DownloadLists[0].StartDownload();
    }

    public static void AddDownload(List<NCSong> songs)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        if (!Timered)
            HyPlayList.OnTimerTicked += Timer_Elapsed;
        songs.ForEach(t => { DownloadLists.Add(new DownloadObject(t)); });
    }

    internal static void AddDownload(ObservableCollection<NCSong> songs)
    {
        if (!CheckDownloadAbilityAndToast()) return;
        if (!Timered)
            HyPlayList.OnTimerTicked += Timer_Elapsed;
        foreach (var song in songs) DownloadLists.Add(new DownloadObject(song));
    }
}

public class UwpStorageFileAbstraction : File.IFileAbstraction
{
    private readonly StorageFile file;


    public UwpStorageFileAbstraction(StorageFile file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        this.file = file;
    }

    public string Name => file.Name;

    public Stream ReadStream => file.OpenStreamForReadAsync().GetAwaiter().GetResult();

    public Stream WriteStream => file.OpenStreamForWriteAsync().GetAwaiter().GetResult();


    public void CloseStream(Stream stream)
    {
        stream?.Dispose();
    }
}
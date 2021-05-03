using System;
using System.Collections.Generic;
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
using Newtonsoft.Json.Linq;
using TagLib;

namespace HyPlayer.HyPlayControl
{
    static class DownloadManager
    {
        public static Dictionary<DownloadOperation, NCPlayItem> DownloadLists = new Dictionary<DownloadOperation, NCPlayItem>();

        public static BackgroundDownloader Downloader = new BackgroundDownloader();


        public static async void AddDownload(NCSong song)
        {
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl, new Dictionary<string, object>() { { "id", song.sid } });
            if (isok)
            {
                if (json["data"][0]["code"].ToString() != "200")
                {
                    return; //未获取到
                }
                string FileName = string.Join(';', song.Artist.Select(t => t.name)) + " - " + song.songname + "." + json["data"][0]["type"].ToString().ToLowerInvariant();
                var dop = Downloader.CreateDownload(new Uri(json["data"][0]["url"].ToString()), await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.downloadDir)).CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting));
                NCPlayItem ncp = new NCPlayItem()
                {
                    bitrate = json["data"][0]["br"].ToObject<int>(),
                    tag = "下载",
                    Album = song.Album,
                    Artist = song.Artist,
                    subext = json["data"][0]["type"].ToString().ToLowerInvariant(),
                    sid = song.sid,
                    songname = song.songname,
                    url = json["data"][0]["url"].ToString(),
                    LengthInMilliseconds = song.LengthInMilliseconds,
                    size = json["data"][0]["size"].ToString(),
                    md5 = json["data"][0]["md5"].ToString()
                };
                DownloadLists[dop] = ncp;
                var process = new Progress<DownloadOperation>(ProgressCallback);
                _ = dop.StartAsync().AsTask(process);
            }
        }

        public static async void AddDownload(List<NCSong> songs)
        {
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl, new Dictionary<string, object>() { { "id", string.Join(',', songs.Select(t => t.sid)) } });
            if (isok)
            {
                if (json["data"][0]["code"].ToString() != "200")
                {
                    return; //未获取到
                }

                int i = 0;
                foreach (JToken jToken in json["data"])
                {
                    var song = songs.Find(t=>t.sid == jToken["id"].ToString());
                    string FileName = string.Join(';', song.Artist.Select(t => t.name)) + " - " + song.songname + "." + jToken["type"].ToString().ToLowerInvariant();
                    var dop = Downloader.CreateDownload(new Uri(jToken["url"].ToString()), await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.downloadDir)).CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting));
                    NCPlayItem ncp = new NCPlayItem()
                    {
                        bitrate = jToken["br"].ToObject<int>(),
                        tag = "下载",
                        Album = song.Album,
                        Artist = song.Artist,
                        subext = jToken["type"].ToString().ToLowerInvariant(),
                        sid = song.sid,
                        songname = song.songname,
                        url = jToken["url"].ToString(),
                        LengthInMilliseconds = song.LengthInMilliseconds,
                        size = jToken["size"].ToString(),
                        md5 = jToken["md5"].ToString()
                    };
                    DownloadLists[dop] = ncp;
                    var process = new Progress<DownloadOperation>(ProgressCallback);
                    _ = dop.StartAsync().AsTask(process);
                    i++;
                }
            }
        }

        private static async void ProgressCallback(DownloadOperation obj)
        {
            if (obj.Progress.BytesReceived == obj.Progress.TotalBytesToReceive)
            {
                if (!DownloadLists.ContainsKey(obj)) return;
                _ = Task.Run((async () =>
                {
                    //下载歌词
                    (bool isOk, Newtonsoft.Json.Linq.JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Lyric,
                          new Dictionary<string, object>() { { "id", DownloadLists[obj].sid } });
                    if (isOk)
                    {
                        if (!(json.ContainsKey("nolyric") && json["nolyric"].ToString().ToLower() == "true") && !(json.ContainsKey("uncollected") && json["uncollected"].ToString().ToLower() == "true"))
                        {
                            var sf = await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.downloadDir)).CreateFileAsync(
                                Path.GetFileName(Path.ChangeExtension(obj.ResultFile.Path, "lrc")), CreationCollisionOption.ReplaceExisting);
                            var lrc = Utils.ConvertPureLyric(json["lrc"]["lyric"].ToString());
                            Utils.ConvertTranslation(json["tlyric"]["lyric"].ToString(), lrc);
                            var lrctxt = string.Join("\r\n", lrc.Select(t =>
                            {
                                if (t.HaveTranslation)
                                    return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric + " 「" + t.Translation + "」";
                                return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric;
                            }));
                            await FileIO.WriteTextAsync(sf, lrctxt);

                        }
                    }


                    //写相关信息
                    var file = TagLib.File.Create(new UwpStorageFileAbstraction((StorageFile)obj.ResultFile));
                    file.Tag.Album = DownloadLists[obj].Album.name;
                    file.Tag.Performers = DownloadLists[obj].Artist.Select(t => t.name).ToArray();
                    file.Tag.Title = DownloadLists[obj].songname;
                    var cover = await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.downloadDir)).CreateFileAsync(
                        Path.GetFileName(Path.ChangeExtension(obj.ResultFile.Path, "cover.jpg")), CreationCollisionOption.ReplaceExisting);
                    var st = (await cover.OpenAsync(FileAccessMode.ReadWrite));
                    RandomAccessStreamReference.CreateFromUri(new Uri(DownloadLists[obj].Album.cover)).OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead().CopyTo(st.AsStreamForWrite());
                    file.Tag.Pictures = new IPicture[] { new Picture(ByteVector.FromFile(new UwpStorageFileAbstraction(cover))) };
                    cover.DeleteAsync();
                    The163KeyHelper.TrySetMusicInfo(file.Tag, DownloadLists[obj]);
                    file.Save();

                    ToastContent downloadToastContent = new ToastContent()
                    {
                        Visual = new ToastVisual()
                        {
                            BindingGeneric = new ToastBindingGeneric()
                            {
                                Children =
                                  {
                                      new AdaptiveText()
                                      {
                                          Text = "下载完成",
                                          HintStyle = AdaptiveTextStyle.Header
                                      },
                                      new AdaptiveText()
                                      {
                                          Text = string.Join(';',DownloadLists[obj].Artist.Select(t => t.name).ToArray())+" - " + DownloadLists[obj].songname

                                      }
                                  }
                            }
                        },
                        Launch = "",
                        Scenario = ToastScenario.Reminder,
                        Audio = new ToastAudio() { Silent = true }
                    };
                    var toast = new ToastNotification(downloadToastContent.GetXml())
                    {
                        Tag = "HyPlayerDownloadDone",
                        Data = new NotificationData()
                    };

                    toast.Data.SequenceNumber = 0;
                    ToastNotifier notifier = ToastNotificationManager.CreateToastNotifier();
                    notifier.Show(toast);


                    //清理
                    DownloadLists.Remove(obj);
                }));


            }
        }
    }

    public class UwpStorageFileAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly StorageFile file;

        public string Name => file.Name;

        public Stream ReadStream
        {
            get
            {
                return file.OpenStreamForReadAsync().GetAwaiter().GetResult();
            }
        }

        public Stream WriteStream
        {
            get
            {
                return file.OpenStreamForWriteAsync().GetAwaiter().GetResult();
            }
        }


        public UwpStorageFileAbstraction(StorageFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            this.file = file;
        }


        public void CloseStream(Stream stream)
        {
            stream?.Dispose();
        }
    }
}

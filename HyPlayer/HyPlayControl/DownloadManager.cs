using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using HyPlayer.Classes;
using Microsoft.Toolkit.Uwp.Notifications;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using TagLib;
using System.Net;
using System.Timers;
using Windows.Networking.BackgroundTransfer;

namespace HyPlayer.HyPlayControl
{
    class DownloadObject
    {
        // 0 - 排队 1 - 下载中 2 - 下载完成
        public int Status = 0;
        public int progress;
        public ulong TotalSize;
        public ulong HavedSize;
        DownloadOperation downloadOperation;
        public string filename;
        public string fullpath;
        NCSong ncsong;

        NCPlayItem dontuseme;
        public DownloadObject(NCSong song)
        {
            ncsong = song;
        }

        private async void Wc_DownloadFileCompleted()
        {
            Status = 2;
            //下载歌词
            (bool isOk, Newtonsoft.Json.Linq.JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Lyric,
                  new Dictionary<string, object>() { { "id", ncsong.sid } });
            if (isOk)
            {
                if (!(json.ContainsKey("nolyric") && json["nolyric"].ToString().ToLower() == "true") && !(json.ContainsKey("uncollected") && json["uncollected"].ToString().ToLower() == "true"))
                {
                    var sf = await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.downloadDir)).CreateFileAsync(
                        Path.GetFileName(Path.ChangeExtension(fullpath, "lrc")), CreationCollisionOption.ReplaceExisting);
                    var lrc = Utils.ConvertPureLyric(json["lrc"]["lyric"].ToString());
                    Utils.ConvertTranslation(json["tlyric"]["lyric"].ToString(), lrc);
                    var lrctxt = string.Join("\r\n", lrc.Select(t =>
                    {
                        if (t.HaveTranslation && !string.IsNullOrEmpty(t.Translation))
                            return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric + " 「" + t.Translation + "」";
                        return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric;
                    }));
                    await FileIO.WriteTextAsync(sf, lrctxt);

                }
            }


            //写相关信息
            var file = TagLib.File.Create(new UwpStorageFileAbstraction(await StorageFile.GetFileFromPathAsync(fullpath)));
            file.Tag.Album = ncsong.Album.name;
            file.Tag.Performers = ncsong.Artist.Select(t => t.name).ToArray();
            file.Tag.Title = ncsong.songname;
            file.Tag.Pictures = new IPicture[] { new Picture(ByteVector.FromStream(RandomAccessStreamReference.CreateFromUri(new Uri(ncsong.Album.cover)).OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead())) };
            The163KeyHelper.TrySetMusicInfo(file.Tag, dontuseme);
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
                                          Text = filename

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
            progress = (int)(obj.Progress.TotalBytesToReceive * 100 / obj.Progress.BytesReceived);
            if (TotalSize == HavedSize)
                Wc_DownloadFileCompleted();
        }

        public static void DownloadStartToast(string songname)
        {
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
                                Text = "下载开始",
                                HintStyle = AdaptiveTextStyle.Header
                            },
                            new AdaptiveText()
                            {
                                Text = songname
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
                Tag = "HyPlayerDownloadStart",
                Data = new NotificationData()
            };

            toast.Data.SequenceNumber = 0;
            ToastNotifier notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(toast);
        }

        public async void StartDownload()
        {
            if (downloadOperation != null || Status == 1) return;
            Status = 1;
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl, new Dictionary<string, object>() { { "id", ncsong.sid } });
            if (isok)
            {
                if (json["data"][0]["code"].ToString() != "200")
                {
                    Status = 0;
                    return; //未获取到
                }
                dontuseme = new NCPlayItem()
                {
                    bitrate = json["data"][0]["br"].ToObject<int>(),
                    tag = "下载",
                    Album = ncsong.Album,
                    Artist = ncsong.Artist,
                    subext = json["data"][0]["type"].ToString().ToLowerInvariant(),
                    sid = ncsong.sid,
                    songname = ncsong.songname,
                    url = json["data"][0]["url"].ToString(),
                    LengthInMilliseconds = ncsong.LengthInMilliseconds,
                    size = json["data"][0]["size"].ToString(),
                    md5 = json["data"][0]["md5"].ToString()
                };
                filename = string.Join(';', ncsong.Artist.Select(t => t.name)) + " - " + ncsong.songname + "." + json["data"][0]["type"].ToString().ToLowerInvariant();
                downloadOperation = DownloadManager.Downloader.CreateDownload(new Uri(json["data"][0]["url"].ToString()), await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.downloadDir)).CreateFileAsync(filename, CreationCollisionOption.ReplaceExisting));
                fullpath = downloadOperation.ResultFile.Path;
                var process = new Progress<DownloadOperation>(Wc_DownloadProgressChanged);
                _ = downloadOperation.StartAsync().AsTask(process);
                DownloadStartToast(filename);
            }
        }
    }

    static class DownloadManager
    {
        public static List<DownloadObject> DownloadLists = new List<DownloadObject>();
        public static BackgroundDownloader Downloader = new BackgroundDownloader();
        public static Timer timer = null;

        public static bool CheckDownloadAbilityAndToast()
        {
            if (ApplicationData.Current.RoamingSettings.Values.ContainsKey("CanDownload")) return true;
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
                                Text = "下载功能已关闭",
                                HintStyle = AdaptiveTextStyle.Header
                            },
                            new AdaptiveText()
                            {
                                Text = "下载功能已关闭"

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
                Tag = "HyPlayerDownloadClose",
                Data = new NotificationData()
            };

            toast.Data.SequenceNumber = 0;
            ToastNotifier notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(toast);
            return false;
        }

        public static void AddDownload(NCSong song)
        {
            if (!CheckDownloadAbilityAndToast()) return;
            if (timer == null)
            {
                timer = new Timer(1000);
                timer.Elapsed += Timer_Elapsed;
                timer.Start();
            }
            DownloadLists.Add(new DownloadObject(song));
        }

        private static void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (DownloadLists.Count == 0) return;
            if (DownloadLists[0].Status == 1) return;
            if (DownloadLists[0].Status == 2)
            {
                DownloadLists.RemoveAt(0);
                if (DownloadLists.Count == 0)
                {
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
                                          Text = "下载全部完成",
                                          HintStyle = AdaptiveTextStyle.Header
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
                        Tag = "HyPlayerDownloadAllDone",
                        Data = new NotificationData()
                    };
                    toast.Data.SequenceNumber = 0;
                    ToastNotifier notifier = ToastNotificationManager.CreateToastNotifier();
                    notifier.Show(toast);
                }
                return;
            }
            if (DownloadLists[0].Status == 0)
            {
                DownloadLists[0].StartDownload();
            }
        }

        public static void AddDownload(List<NCSong> songs)
        {
            if (!CheckDownloadAbilityAndToast()) return;
            if (timer == null)
            {
                timer = new Timer(1000);
                timer.Elapsed += Timer_Elapsed;
                timer.Start();
            }
            songs.ForEach(t =>
            {
                DownloadLists.Add(new DownloadObject(t));
            });
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

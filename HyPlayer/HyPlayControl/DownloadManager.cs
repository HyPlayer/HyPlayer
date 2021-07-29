using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using HyPlayer.Classes;
using Microsoft.Toolkit.Uwp.Notifications;
using NeteaseCloudMusicApi;
using TagLib;
using File = TagLib.File;
using System.Threading.Tasks;

namespace HyPlayer.HyPlayControl
{
    internal class DownloadObject
    {
        private NCPlayItem dontuseme;
        public DownloadOperation downloadOperation;
        private string _filename;
        public string filename
        {
            set
            {
                _filename = value.Replace("\\", "＼").Replace("/", "／").Replace(":", "：").Replace("?", "？").Replace("\"", "＂").Replace("<", "＜").Replace(">", "＞").Replace("|", "｜");
            }
            get => _filename;
        }
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

        private void Wc_DownloadFileCompleted()
        {
            Status = 2;
            _ = Task.Run(() =>
              {
                  Common.Invoke(async () =>
                  {
                    //下载歌词
                    var (isOk, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Lyric,
                          new Dictionary<string, object> { { "id", ncsong.sid } });
                      if (isOk)
                          if (!(json.ContainsKey("nolyric") && json["nolyric"].ToString().ToLower() == "true") &&
                              !(json.ContainsKey("uncollected") && json["uncollected"].ToString().ToLower() == "true"))
                          {
                              var sf = await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.downloadDir))
                                  .CreateFileAsync(
                                      Path.GetFileName(Path.ChangeExtension(fullpath, "lrc")),
                                      CreationCollisionOption.ReplaceExisting);
                              var lrc = Utils.ConvertPureLyric(json["lrc"]["lyric"].ToString());
                              Utils.ConvertTranslation(json["tlyric"]["lyric"].ToString(), lrc);
                              var lrctxt = string.Join("\r\n", lrc.Select(t =>
                              {
                                  if (t.HaveTranslation && !string.IsNullOrEmpty(t.Translation))
                                      return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric + " 「" +
                                             t.Translation + "」";
                                  return "[" + t.LyricTime.ToString(@"mm\:ss\.ff") + "]" + t.PureLyric;
                              }));
                              await FileIO.WriteTextAsync(sf, lrctxt);
                          }


                    //写相关信息
                    var file = File.Create(new UwpStorageFileAbstraction(await StorageFile.GetFileFromPathAsync(fullpath)));
                      file.Tag.Album = ncsong.Album.name;
                      file.Tag.Performers = ncsong.Artist.Select(t => t.name).ToArray();
                      file.Tag.Title = ncsong.songname;
                      file.Tag.Pictures = new IPicture[]
                      {
                new Picture(ByteVector.FromStream(RandomAccessStreamReference
                    .CreateFromUri(new Uri(ncsong.Album.cover + "?param=" + StaticSource.PICSIZE_DOWNLOAD_ALBUMCOVER))
                    .OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead()))
                      };
                      file.Tag.Pictures[0].MimeType = "image/jpeg";
                      file.Tag.Pictures[0].Description = "cover.jpg";
                      The163KeyHelper.TrySetMusicInfo(file.Tag, dontuseme);
                      file.Save();

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
                  });
              });
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
        }

        public async void StartDownload()
        {
            if (downloadOperation != null || Status == 1) return;
            Status = 1;
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongUrl,
                new Dictionary<string, object> { { "id", ncsong.sid } });
            if (isok)
            {
                if (json["data"][0]["code"].ToString() != "200")
                {
                    Status = 0;
                    return; //未获取到
                }

                dontuseme = new NCPlayItem
                {
                    bitrate = json["data"][0]["br"].ToObject<int>(),
                    tag = "下载",
                    Album = ncsong.Album,
                    Artist = ncsong.Artist,
                    subext = json["data"][0]["type"].ToString().ToLowerInvariant(),
                    id = ncsong.sid,
                    songname = ncsong.songname,
                    Type = HyPlayItemType.Netease,
                    url = json["data"][0]["url"].ToString(),
                    LengthInMilliseconds = ncsong.LengthInMilliseconds,
                    size = json["data"][0]["size"].ToString(),
                    md5 = json["data"][0]["md5"].ToString()
                };
                filename = string.Join(';', ncsong.Artist.Select(t => t.name)) + " - " + ncsong.songname + "." +
                           json["data"][0]["type"].ToString().ToLowerInvariant();
                downloadOperation = DownloadManager.Downloader.CreateDownload(
                    new Uri(json["data"][0]["url"].ToString()),
                    await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.downloadDir)).CreateFileAsync(
                        filename, CreationCollisionOption.ReplaceExisting));
                fullpath = downloadOperation.ResultFile.Path;
                var process = new Progress<DownloadOperation>(Wc_DownloadProgressChanged);
                _ = downloadOperation.StartAsync().AsTask(process);
                DownloadStartToast(filename);
            }
        }
    }

    internal static class DownloadManager
    {
        private static bool Timered = false;
        public static List<DownloadObject> DownloadLists = new List<DownloadObject>();
        public static BackgroundDownloader Downloader = new BackgroundDownloader();

        public static bool CheckDownloadAbilityAndToast()
        {
            if (ApplicationData.Current.RoamingSettings.Values.ContainsKey("CanDownload")) return true;
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
                                Text = "下载功能已关闭",
                                HintStyle = AdaptiveTextStyle.Header
                            },
                            new AdaptiveText
                            {
                                Text = "下载功能已关闭"
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
                Tag = "HyPlayerDownloadClose",
                Data = new NotificationData()
            };

            toast.Data.SequenceNumber = 0;
            var notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(toast);
            return false;
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
                if (DownloadLists.Count == 0)
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
                }

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
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using TagLib;

namespace HyPlayer.HyPlayControl
{
    static class DownloadManager
    {
        public static Dictionary<DownloadOperation, NCSong> DownloadLists = new Dictionary<DownloadOperation, NCSong>();
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
                DownloadLists[dop] = song;
                var process = new Progress<DownloadOperation>(ProgressCallback);
                _ = dop.StartAsync().AsTask(process);
            }
        }

        private static async void ProgressCallback(DownloadOperation obj)
        {
            if (obj.Progress.BytesReceived == obj.Progress.TotalBytesToReceive)
            {



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
                      file.Save();
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

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using TagLib;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyPlayer.Domain.Music
{
    public partial class PlayItem : IDisposable
    {
        private bool disposedValue;
        public AudioGraphPlaybackSource AudioGraphPlaybackSource { get; set; }
        public InMemoryRandomAccessStream NcmPlayableStream { get; set; }
        public string NcmPlayableStreamMIMEType { get; set; } = string.Empty;
        public IBuffer? CoverBuffer { get; set; }

        public PlayItem()
        {

        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    AudioGraphPlaybackSource?.Dispose();
                }
                NcmPlayableStream?.Dispose();
                NcmPlayableStreamMIMEType = null;
                AudioGraphPlaybackSource = null;
                NcmPlayableStream = null;
                disposedValue = true;
            }
        }

        ~PlayItem()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
    public enum HyPlayItemType
    {
        Local,
        LocalProgressive,
        Netease,
        Radio
    }
    public class HyPlayItem : IEquatable<HyPlayItem>
    {
        public PlayItem PlayItem { get; set; }
        public HyPlayItemType ItemType { get; set; }
        public NCAlbum Album { get; set; }
        public List<NCArtist> Artist { get; set; }
        public int Bitrate { get; set; }
        public string CDName { get; set; }
        public string Translation { get; set; }
        public StorageFile LocalStorageFile { get; set; } //如非特殊原因请不要设置这个东西!
        public Tag LocalFileTag { get; set; }
        public string Id { get; set; }
        public bool IsLocalFile { get; set; }
        public double LengthInMilliseconds { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public string SubExt { get; set; }
        public string QualityTag { get; set; }
        public string InfoTag { get; set; }
        public int TrackId { get; set; }
        public string Url { get; set; }

        /// <summary>
        /// 三字母媒体源提供者标识，用于兼容旧 UI 模型中的播放来源。
        /// <list type="bullet">
        ///   <item><c>lcl</c> — 普通本地音频文件</item>
        ///   <item><c>ncm</c> — NCM 加密文件（解密后播放）</item>
        ///   <item><c>nca</c> — 网易云在线播放 + 缓存策略（边下边播）</item>
        ///   <item><c>nst</c> — 网易云纯流式播放（不缓存）</item>
        /// </list>
        /// </summary>
        public string ProviderId
        {
            get
            {
                switch (ItemType)
                {
                    case HyPlayItemType.Local:
                    case HyPlayItemType.LocalProgressive:
                        if (SubExt == ".ncm") return "ncm";
                        return "lcl";
                    case HyPlayItemType.Netease:
                    case HyPlayItemType.Radio:
                        if (LocalStorageFile != null) return "ncm";
                        return Ioc.Default.GetRequiredService<Setting>().enableCache ? "nca" : "nst";
                    default:
                        throw new NotImplementedException($"未知的媒体源类型：{ItemType}");
                }
            }
        }

        public double? Volume { get; set; }

        public string ArtistString
        {
            get { return string.Join("; ", Artist.Select(t => t.Name)); }
        }
        public string AlbumString => Album.Name ?? "未知专辑";

        public string GetQualityTagText(string fallbackLevel = null)
        {
            if (!string.IsNullOrWhiteSpace(QualityTag)) return QualityTag;
            if (IsLocalFile) return "本地歌曲";
            return FormatAudioLevel(fallbackLevel);
        }

        public static string FormatAudioLevel(string level)
        {
            return level switch
            {
                "standard" => "标准",
                "higher" => "较高",
                "exhigh" => "极高",
                "lossless" => "无损",
                "hires" => "Hi-Res",
                "jyeffect" => "高清环绕声",
                "sky" => "沉浸环绕声",
                "jymaster" => "超清母带",
                _ => string.Empty
            };
        }

        public bool Equals(HyPlayItem other)
        {
            if (ItemType == HyPlayItemType.Local || ItemType == HyPlayItemType.LocalProgressive)
                return other.LocalStorageFile == LocalStorageFile || Url == other.Url;
            return Id == other.Id;
        }

        public NCSong ToNCSong()
        {
            return new NCSong
            {
                Type = ItemType,
                Album = Album,
                Artist = Artist,
                LengthInMilliseconds = LengthInMilliseconds,
                SongId = Id,
                SongName = Name,
                TrackId = TrackId
            };
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as HyPlayItem);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}

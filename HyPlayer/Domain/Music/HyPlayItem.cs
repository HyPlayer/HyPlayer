using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.IO;
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

        private const string NeteaseProviderId = "ncm";
        private const string NeteaseSongTypeId = "sg";
        private const string NeteaseRadioTypeId = "dj";
        private const string LocalProviderId = "lcl";
        private const string LocalSongTypeId = "sg";
        private const string LocalNcmSongTypeId = "ncm";

        public string ProviderIdentityProviderId { get; set; }
        public string ProviderIdentityTypeId { get; set; }
        public string ProviderIdentityActualId { get; set; }

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

        public static HyPlayItem FromProviderItem(ProvidableItemBase item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return item is SingleSongBase song ? FromProviderSong(song) : CreateProviderBackedItem(item);
        }

        public static HyPlayItem FromProviderSong(SingleSongBase song)
        {
            ArgumentNullException.ThrowIfNull(song);

            var playItem = CreateProviderBackedItem(song);
            var creators = GetCompletedCreators(song);
            playItem.LengthInMilliseconds = song.Duration;
            playItem.Album = ToLegacyAlbum(song.Album, GetCompletedCoverUri(song)?.ToString());
            playItem.Artist = ToLegacyArtists(song, creators);
            playItem.Translation = song is IHasTranslation translatedSong ? translatedSong.Translation ?? string.Empty : string.Empty;
            playItem.CDName = string.Empty;
            playItem.TrackId = 0;
            return playItem;
        }

        public (string ProviderId, string TypeId, string ActualId) GetItemIdentity()
        {
            if (!string.IsNullOrWhiteSpace(ProviderIdentityProviderId) ||
                !string.IsNullOrWhiteSpace(ProviderIdentityTypeId) ||
                !string.IsNullOrWhiteSpace(ProviderIdentityActualId))
            {
                return (ProviderIdentityProviderId ?? string.Empty,
                    ProviderIdentityTypeId ?? string.Empty,
                    ProviderIdentityActualId ?? string.Empty);
            }

            return (GetFallbackProviderId(this), GetFallbackTypeId(this), GetFallbackActualId(this));
        }

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

        public override bool Equals(object obj)
        {
            return Equals(obj as HyPlayItem);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        private static HyPlayItem CreateProviderBackedItem(ProvidableItemBase item)
        {
            return new HyPlayItem
            {
                ItemType = GetFallbackItemType(item.ProviderId, item.TypeId),
                Id = item.ActualId ?? string.Empty,
                Name = item.Name ?? string.Empty,
                Album = new NCAlbum { AlbumType = GetFallbackItemType(item.ProviderId, item.TypeId), Id = string.Empty, Name = string.Empty, Cover = string.Empty },
                Artist = [],
                CDName = string.Empty,
                Translation = string.Empty,
                SubExt = string.Empty,
                QualityTag = string.Empty,
                InfoTag = string.Empty,
                Url = string.Empty,
                ProviderIdentityProviderId = item.ProviderId,
                ProviderIdentityTypeId = item.TypeId,
                ProviderIdentityActualId = item.ActualId ?? string.Empty
            };
        }

        private static NCAlbum ToLegacyAlbum(AlbumBase album, string coverIdentity)
        {
            if (album is null)
                return new NCAlbum { AlbumType = HyPlayItemType.Netease, Id = string.Empty, Name = string.Empty, Cover = coverIdentity ?? string.Empty };

            return new NCAlbum
            {
                AlbumType = GetFallbackItemType(album.ProviderId, album.TypeId),
                Id = album.ActualId ?? string.Empty,
                Name = album.Name ?? string.Empty,
                Cover = coverIdentity ?? string.Empty
            };
        }

        private static List<NCArtist> ToLegacyArtists(SingleSongBase song, IReadOnlyList<PersonBase> creators)
        {
            if (creators is { Count: > 0 })
            {
                return creators.Select(creator => new NCArtist
                {
                    Id = creator.ActualId ?? string.Empty,
                    Name = creator.Name ?? string.Empty,
                    Type = GetFallbackItemType(creator.ProviderId, creator.TypeId)
                }).ToList();
            }

            return song.CreatorList?.Select(creatorName => new NCArtist
            {
                Name = creatorName,
                Id = string.Empty,
                Type = HyPlayItemType.Netease
            }).ToList() ?? [];
        }

        private static IReadOnlyList<PersonBase> GetCompletedCreators(SingleSongBase song)
        {
            var creatorsTask = song.GetCreatorsAsync();
            return creatorsTask.IsCompletedSuccessfully ? creatorsTask.Result : null;
        }

        private static Uri GetCompletedCoverUri(SingleSongBase song)
        {
            if (song is not IHasCover coverProvider) return null;

            var coverTask = coverProvider.GetCoverAsync();
            if (!coverTask.IsCompletedSuccessfully || coverTask.Result is not IResourceResultOf<Uri> uriResult) return null;

            var uriTask = uriResult.GetResourceAsync();
            return uriTask.IsCompletedSuccessfully ? uriTask.Result : null;
        }

        private static HyPlayItemType GetFallbackItemType(string providerId, string typeId)
        {
            if (providerId == LocalProviderId) return HyPlayItemType.Local;
            if (typeId is NeteaseRadioTypeId or "pr") return HyPlayItemType.Radio;
            return HyPlayItemType.Netease;
        }

        private static string GetFallbackProviderId(HyPlayItem item)
        {
            if (item.IsLocalFile || item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
                return LocalProviderId;

            return item.ItemType is HyPlayItemType.Netease or HyPlayItemType.Radio ? NeteaseProviderId : item.ProviderId;
        }

        private static string GetFallbackTypeId(HyPlayItem item)
        {
            if (item.IsLocalFile || item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
                return IsLocalNcmFile(item) ? LocalNcmSongTypeId : LocalSongTypeId;

            return item.ItemType switch
            {
                HyPlayItemType.Radio => NeteaseRadioTypeId,
                _ => NeteaseSongTypeId
            };
        }

        private static string GetFallbackActualId(HyPlayItem item)
        {
            if (item.IsLocalFile || item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
                return item.LocalStorageFile?.Path ?? item.Url ?? item.Id ?? string.Empty;

            return item.Id ?? string.Empty;
        }

        private static bool IsLocalNcmFile(HyPlayItem item)
        {
            return string.Equals(item.LocalStorageFile?.FileType, ".ncm", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(item.SubExt, ".ncm", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(Path.GetExtension(item.Url), ".ncm", StringComparison.OrdinalIgnoreCase);
        }
    }
}

#nullable enable
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Services.Abstractions;
using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TagLib.Ape;
using TagLib.Matroska;
using Windows.Media.Core;
using Windows.Storage;

namespace HyPlayer.Services.Playback.MediaProviders;

/// <summary>
/// <c>lcl</c> — 普通本地音频文件提供者。
/// <para>
/// 从本地路径获取 <see cref="StorageFile"/>，通过
/// <see cref="MediaSource.CreateFromStorageFile"/> 创建媒体源。
/// </para>
/// </summary>
public sealed class LocalFileProvider : IMediaSourceProvider
{
    /// <inheritdoc />
    public string Id => "lcl";

    /// <inheritdoc />
    public async Task<MediaSource?> CreateAsync(HyPlayItem item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var file = item.LocalStorageFile;
        if (file == null && !string.IsNullOrEmpty(item.Url))
        {
            file = await StorageFile.GetFileFromPathAsync(item.Url);
            item.LocalStorageFile = file;
        }

        if (file == null)
            return null;
        using var abstraction = new UwpStorageFileAbstraction(file);
        using var tagFile = TagLibHelper.Create(abstraction, file.FileType);
        if(item.ItemType == HyPlayItemType.LocalProgressive)
        {
            var songPerformersList = tagFile.Tag.Performers
                .Select(t => new NCArtist { Name = t, Type = HyPlayItemType.Local }).ToList();
            if (songPerformersList.Count == 0)
                songPerformersList.Add(new NCArtist { Name = "未知歌手", Type = HyPlayItemType.Local });

            item.IsLocalFile = true;
            item.LocalFileTag = tagFile.Tag;
            item.Bitrate = tagFile.Properties.AudioBitrate;
            item.Name = tagFile.Tag.Title;
            item.Artist = songPerformersList;
            item.Album = new NCAlbum { Name = tagFile.Tag.Album };
            item.TrackId = (int)tagFile.Tag.Track;
            item.CDName = "01";
            item.Size = 0;
            item.LengthInMilliseconds = tagFile.Properties.Duration.TotalMilliseconds;
            item.ItemType = HyPlayItemType.Local;
        }
        item.PlayItem ??= new PlayItem();
        item.PlayItem.CoverBuffer = tagFile.Tag.Pictures[0]?.Data?.Data?.AsBuffer();
        return MediaSource.CreateFromStorageFile(file);
    }
}

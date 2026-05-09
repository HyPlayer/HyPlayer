using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Services.Abstractions;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;

namespace HyPlayer.Services.Playback;

/// <inheritdoc />
public sealed class LocalFileImportService : ILocalFileImportService
{
    private readonly INotificationService _notification;
    private readonly Setting _setting;

    public LocalFileImportService(INotificationService notification, Setting setting)
    {
        _notification = notification;
        _setting = setting;
    }

    /// <inheritdoc />
    public async Task<IList<HyPlayItem>> PickLocalFilesAsync()
    {
        var fop = new FileOpenPicker();
        fop.FileTypeFilter.Add(".flac");
        fop.FileTypeFilter.Add(".mp3");
        fop.FileTypeFilter.Add(".ncm");
        fop.FileTypeFilter.Add(".ape");
        fop.FileTypeFilter.Add(".m4a");
        fop.FileTypeFilter.Add(".wav");

        var files = await fop.PickMultipleFilesAsync();
        if (files == null || files.Count == 0)
            return [];

        var items = new List<HyPlayItem>();
        foreach (var file in files)
        {
            try
            {
                await RegisterFutureAccessAsync(file);
                items.Add(await LoadStorageFileAsync(file));
            }
            catch (Exception ex)
            {
                _notification.ShowMessage($"加载文件 {file.Name} 失败", ex.Message);
            }
        }

        return items;
    }

    /// <inheritdoc />
    public async Task RegisterFutureAccessAsync(StorageFile file)
    {
        var folder = await file.GetParentAsync();
        if (folder != null)
        {
            var token = folder.Path.GetHashCode().ToString();
            if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
                StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, folder);
        }
        else
        {
            var token = file.Path.GetHashCode().ToString();
            if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
                StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, file);
        }
    }

    /// <inheritdoc />
    public async Task<HyPlayItem> LoadStorageFileAsync(StorageFile sf, bool nocheck163 = false)
    {
        if (string.Equals(Path.GetExtension(sf.Path), ".ncm", StringComparison.OrdinalIgnoreCase))
            return await LoadNcmStorageFileAsync(sf);

        using var abstraction = new UwpStorageFileAbstraction(sf);
        using var tagFile = TagLibHelper.Create(abstraction, sf.FileType);
        if (nocheck163 || !The163KeyHelper.TryGetMusicInfo(tagFile.Tag, out var mi))
        {
            var songPerformersList = tagFile.Tag.Performers
                .Select(t => new NCArtist { Name = t, Type = HyPlayItemType.Local }).ToList();
            if (songPerformersList.Count == 0)
                songPerformersList.Add(new NCArtist { Name = "未知歌手", Type = HyPlayItemType.Local });

            var hyPlayItem = new HyPlayItem
            {
                IsLocalFile = true,
                LocalFileTag = tagFile.Tag,
                Bitrate = tagFile.Properties.AudioBitrate,
                InfoTag = sf.Provider.DisplayName,
                Id = null,
                Name = tagFile.Tag.Title,
                Artist = songPerformersList,
                Album = new NCAlbum { Name = tagFile.Tag.Album },
                TrackId = (int)tagFile.Tag.Track,
                CDName = "01",
                Url = sf.Path,
                SubExt = sf.FileType,
                Size = 0,
                LengthInMilliseconds = tagFile.Properties.Duration.TotalMilliseconds,
                ItemType = HyPlayItemType.Local
            };
            if (sf.Provider.Id == "network" || _setting.safeFileAccess)
                hyPlayItem.LocalStorageFile = sf;
            return hyPlayItem;
        }

        if (string.IsNullOrEmpty(mi.musicName))
            return await LoadStorageFileAsync(sf, true);

        return new HyPlayItem
        {
            ItemType = HyPlayItemType.Local,
            Album = new NCAlbum { Name = mi.album, Id = mi.albumId.ToString(), Cover = mi.albumPic },
            Url = sf.Path,
            SubExt = sf.FileType,
            LocalFileTag = tagFile.Tag,
            Bitrate = mi.bitrate,
            IsLocalFile = true,
            LengthInMilliseconds = tagFile.Properties.Duration.TotalMilliseconds,
            Id = mi.musicId.ToString(),
            Artist = [.. mi.artist.Select(t => new NCArtist { Name = t[0].ToString(), Id = t[1].ToString() })],
            Name = mi.musicName,
            LocalStorageFile = sf,
            TrackId = (int)tagFile.Tag.Track,
            CDName = "01",
            InfoTag = sf.Provider.DisplayName
        };
    }

    private static async Task<HyPlayItem> LoadNcmStorageFileAsync(StorageFile file)
    {
        using var stream = await file.OpenStreamForReadAsync();
        if (!NCMFile.IsCorrectNCMFile(stream))
            throw new InvalidDataException("不是有效的 NCM 文件");

        var info = NCMFile.GetNCMMusicInfo(stream);
        var hyitem = new HyPlayItem
        {
            ItemType = HyPlayItemType.Netease,
            Album = new NCAlbum
            {
                Name = info.album,
                Id = info.albumId.ToString(),
                Cover = info.albumPic
            },
            LocalStorageFile = file,
            Url = file.Path,
            SubExt = info.format,
            Bitrate = info.bitrate,
            IsLocalFile = true,
            LengthInMilliseconds = info.duration,
            Id = info.musicId.ToString(),
            TrackId = -1,
            CDName = "01",
            Artist = null,
            Name = info.musicName,
            InfoTag = file.Provider.DisplayName + " NCM"
        };
        hyitem.Artist = [.. info.artist.Select(t => new NCArtist
        { Name = t[0].ToString(), Id = t[1].ToString() })];
        return hyitem;
    }
}

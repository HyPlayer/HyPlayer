using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.LocalMusic;
using HyPlayer.Platform.Playback.LocalProvider;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.Storage.Audio;
using UwpStorageFileAbstraction = HyPlayer.Platform.Storage.Audio.UwpStorageFileAbstraction;

namespace HyPlayer.Features.Playback.Services;

/// <inheritdoc />
public sealed class LocalFileImportService : ILocalFileImportService
{
    private readonly INotificationService _notification;

    public LocalFileImportService(INotificationService notification)
    {
        _notification = notification;
    }

    /// <inheritdoc />
    public async Task<IList<LocalSong>> PickLocalFilesAsync()
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

        var items = new List<LocalSong>();
        foreach (var file in files)
            try
            {
                await RegisterFutureAccessAsync(file);
                items.Add(await LoadStorageFileAsync(file));
            }
            catch (Exception ex)
            {
                _notification.ShowMessage($"加载文件 {file.Name} 失败", ex.Message);
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
    public async Task<LocalSong> LoadStorageFileAsync(StorageFile sf, bool nocheck163 = false)
    {
        if (string.Equals(Path.GetExtension(sf.Path), ".ncm", StringComparison.OrdinalIgnoreCase))
            return await LoadNcmStorageFileAsync(sf);

        using var abstraction = new UwpStorageFileAbstraction(sf);
        using var tagFile = TagLibHelper.Create(abstraction, sf.FileType);
        if (nocheck163 || !The163KeyHelper.TryGetMusicInfo(tagFile.Tag, out var mi))
        {
            var songPerformersList = tagFile.Tag.Performers
                .Select(t => new LocalArtist { Name = t, ActualId = t }).ToList();
            if (songPerformersList.Count == 0)
                songPerformersList.Add(new LocalArtist { Name = "未知歌手", ActualId = string.Empty });

            return new LocalSong
            {
                StorageFile = sf.Provider.Id == "network" ? sf : null,
                FileTag = tagFile.Tag,
                Bitrate = tagFile.Properties.AudioBitrate,
                InfoTag = sf.Provider.DisplayName,
                Name = tagFile.Tag.Title,
                CreatorList = songPerformersList.Select(t => t.Name ?? string.Empty).ToList(),
                Artists = songPerformersList,
                Album = new LocalAlbum { Name = tagFile.Tag.Album, ActualId = string.Empty },
                TrackNumber = (int)tagFile.Tag.Track,
                CdName = "01",
                ActualId = sf.Path,
                ExtensionName = sf.FileType,
                Duration = (long)tagFile.Properties.Duration.TotalMilliseconds,
                Available = true
            };
        }

        if (string.IsNullOrEmpty(mi.MusicName))
            return await LoadStorageFileAsync(sf, true);

        var artists = mi.Artist
            .Select(t => new LocalArtist { Name = t[0].ToString(), ActualId = t[1].ToString() })
            .ToList();

        return new LocalSong
        {
            Album = new LocalAlbum { Name = mi.Album, ActualId = mi.AlbumId.ToString() },
            ActualId = sf.Path,
            ExtensionName = sf.FileType,
            FileTag = tagFile.Tag,
            Bitrate = mi.Bitrate,
            Duration = (long)tagFile.Properties.Duration.TotalMilliseconds,
            CreatorList = artists.Select(t => t.Name ?? string.Empty).ToList(),
            Artists = artists,
            Name = mi.MusicName,
            StorageFile = sf,
            TrackNumber = (int)tagFile.Tag.Track,
            CdName = "01",
            InfoTag = sf.Provider.DisplayName,
            Available = true
        };
    }

    private static async Task<LocalSong> LoadNcmStorageFileAsync(StorageFile file)
    {
        using var stream = await file.OpenStreamForReadAsync();
        if (!NCMFile.IsCorrectNCMFile(stream))
            throw new InvalidDataException("不是有效的 NCM 文件");

        var info = NCMFile.GetNCMMusicInfo(stream);
        var artists = info.Artist
            .Select(t => new LocalArtist { Name = t[0].ToString(), ActualId = t[1].ToString() })
            .ToList();

        return new LocalSong
        {
            IsNcm = true,
            Album = new LocalAlbum { Name = info.Album, ActualId = info.AlbumId.ToString() },
            StorageFile = file,
            ActualId = file.Path,
            ExtensionName = info.Format,
            Bitrate = info.Bitrate,
            Duration = (long)info.Duration,
            TrackNumber = -1,
            CdName = "01",
            CreatorList = artists.Select(t => t.Name ?? string.Empty).ToList(),
            Artists = artists,
            Name = info.MusicName,
            InfoTag = file.Provider.DisplayName + " NCM",
            Available = true
        };
    }
}

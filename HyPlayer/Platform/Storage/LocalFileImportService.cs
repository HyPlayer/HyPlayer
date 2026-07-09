using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Platform.Storage.Audio;
using HyPlayer.NeteaseProvider.LocalMusic;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.Platform.Playback.LocalProvider;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using UwpStorageFileAbstraction = HyPlayer.Platform.Storage.Audio.UwpStorageFileAbstraction;

namespace HyPlayer.Features.Playback.Services;

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

        if (string.IsNullOrEmpty(mi.musicName))
            return await LoadStorageFileAsync(sf, true);

        var artists = mi.artist
            .Select(t => new LocalArtist { Name = t[0].ToString(), ActualId = t[1].ToString() })
            .ToList();

        return new LocalSong
        {
            Album = new LocalAlbum { Name = mi.album, ActualId = mi.albumId.ToString() },
            ActualId = sf.Path,
            ExtensionName = sf.FileType,
            FileTag = tagFile.Tag,
            Bitrate = mi.bitrate,
            Duration = (long)tagFile.Properties.Duration.TotalMilliseconds,
            CreatorList = artists.Select(t => t.Name ?? string.Empty).ToList(),
            Artists = artists,
            Name = mi.musicName,
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
        var artists = info.artist
            .Select(t => new LocalArtist { Name = t[0].ToString(), ActualId = t[1].ToString() })
            .ToList();

        return new LocalSong
        {
            IsNcm = true,
            Album = new LocalAlbum { Name = info.album, ActualId = info.albumId.ToString() },
            StorageFile = file,
            ActualId = file.Path,
            ExtensionName = info.format,
            Bitrate = info.bitrate,
            Duration = (long)info.duration,
            TrackNumber = -1,
            CdName = "01",
            CreatorList = artists.Select(t => t.Name ?? string.Empty).ToList(),
            Artists = artists,
            Name = info.musicName,
            InfoTag = file.Provider.DisplayName + " NCM",
            Available = true
        };
    }
}

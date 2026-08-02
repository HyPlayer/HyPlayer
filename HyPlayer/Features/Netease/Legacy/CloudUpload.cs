using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Platform.Storage.Audio;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Resources;

namespace HyPlayer.Features.Netease.Legacy;

internal class CloudUpload
{
#nullable enable
    public static async Task UploadMusic(StorageFile file)
    {
        var notification = Ioc.Default.GetRequiredService<INotificationService>();
        var cloudUploadProvider = Ioc.Default.GetRequiredService<ICloudUploadProvidable>();
        notification.ShowMessage("上传本地音乐至音乐云盘中", "正在上传: " + file.DisplayName);

        try
        {
            var metadata = await CreateMetadataAsync(file);
            await cloudUploadProvider.UploadCloudLibraryItemAsync(new StorageFileCloudUploadResource(file), metadata);
            notification.ShowMessage("上传本地音乐至音乐云盘成功", "成功上传: " + file.DisplayName);
        }
        catch (Exception ex)
        {
            notification.ShowMessage($"上传失败: {file.DisplayName}", ex.Message);
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> CreateMetadataAsync(StorageFile file)
    {
        var musicProperties = await file.Properties.GetMusicPropertiesAsync();
        using var abstraction = new UwpStorageFileAbstraction(file);
        var album = string.Empty;
        var title = file.DisplayName;
        var artist = string.Empty;
        byte[]? coverBytes = null;

        try
        {
            using var tagFile = TagLibHelper.Create(abstraction, file.FileType);
            var tag = tagFile?.Tag;
            album = tag?.Album ?? string.Empty;
            title = string.IsNullOrWhiteSpace(tag?.Title) ? file.DisplayName : tag!.Title;
            artist = string.Join("; ", tag?.Performers ?? []);
            coverBytes = tag?.Pictures?.FirstOrDefault()?.Data?.Data;
        }
        catch
        {
            // ignored
        }

        var metadata = new Dictionary<string, string>
        {
            ["fileName"] = file.Name,
            ["title"] = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(file.Path) : title,
            ["album"] = album,
            ["artist"] = artist,
            ["bitrate"] = ((int)musicProperties.Bitrate).ToString(),
            ["durationMs"] = ((long)musicProperties.Duration.TotalMilliseconds).ToString(),
            ["extension"] = file.FileType,
            ["contentType"] = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType
        };

        if (coverBytes is { Length: > 0 })
            metadata["coverBase64"] = Convert.ToBase64String(coverBytes);

        return metadata;
    }

    private sealed class StorageFileCloudUploadResource(StorageFile file) : ResourceBase
    {
        public override ResourceType Type => ResourceType.Audio;

        public override Task<ResourceResultBase> GetResourceAsync(ResourceQualityTag? qualityTag = null,
            CancellationToken ctk = default)
        {
            return Task.FromResult<ResourceResultBase>(new StorageFileCloudUploadResourceResult(file)
            {
                ResourceStatus = ResourceStatus.Success
            });
        }
    }

    private sealed class StorageFileCloudUploadResourceResult(StorageFile file)
        : ResourceResultBase, IResourceResultOf<Stream>
    {
        public override Exception? ExternalException { get; init; }
        public override required ResourceStatus ResourceStatus { get; init; }

        public async Task<Stream?> GetResourceAsync(CancellationToken cancellationToken = default)
        {
            var fileStream = await file.OpenAsync(FileAccessMode.Read);
            return fileStream.AsStream();
        }
    }
}
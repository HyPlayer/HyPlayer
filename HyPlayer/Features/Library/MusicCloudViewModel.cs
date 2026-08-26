using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.UI.Lists;

namespace HyPlayer.Features.Library;

public partial class MusicCloudViewModel(
    IContainerItemManagementProvidable containerItemManagement,
    INotificationService notification,
    ApiSettings settings,
    IUserLibraryProvidable userLibraryProvider,
    IUserLibraryTypeIds userLibraryTypeIds) : ObservableObject
{
    [ObservableProperty] public partial ContainerBase? ContentContainer { get; set; }

    public bool GreedyLoad => settings.GreedilyLoadPlayContainerItems;

    public async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        if (await userLibraryProvider.GetCurrentUserLibraryContainerAsync(
                userLibraryTypeIds.CloudLibraryTypeId,
                cancellationToken) is not ContainerBase container)
            return false;

        ContentContainer = container;
        return true;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await SimpleCacher.ResetCacheAsync(CacheType.Login, "userCloud_", true);
        if (!await LoadAsync(cancellationToken))
            notification.ShowMessage("刷新云盘失败", "服务端未返回云盘信息");
    }

    public async Task DeleteItemAsync(
        ProvidableItemRowViewModel row,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.ItemId))
            return;

        try
        {
            await containerItemManagement.RemoveItemFromContainerAsync(
                userLibraryTypeIds.CloudLibraryTypeId,
                row.ActualId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            notification.ShowMessage("删除云盘歌曲失败", ex.Message);
            return;
        }

        notification.ShowMessage("已从云盘删除", row.Title);
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notification.ShowMessage("歌曲已删除，但刷新云盘失败", ex.Message);
        }
    }

    public async Task UploadAsync(IReadOnlyList<StorageFile> files)
    {
        if (files.Count == 0)
            return;

        notification.ShowMessage("请稍等", $"正在上传 {files.Count} 个音乐文件");
        for (var index = 0; index < files.Count; index++)
        {
            notification.ShowMessage(
                $"正在上传共 {files.Count} 个音乐文件",
                $"正在上传第 {index + 1} 个音乐文件");
            await CloudUpload.UploadMusic(files[index]);
        }

        notification.ShowMessage("上传完成", "请重新加载云盘页面");
    }
}

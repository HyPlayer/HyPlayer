#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Threading;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Shell.Navigation.Services;

namespace HyPlayer.Features.Settings.Services;

public static class FolderAccessGuard
{
    private static readonly TimeSpan DialogRetryDelay = TimeSpan.FromSeconds(3);
    private static int _watching;
    private static int _checking;

    public static async Task<bool> EnsureWritableAsync(StorageFolder folder, string what)
    {
        if (await CanWriteAsync(folder)) return true;
        var dialog = new ContentDialog
        {
            Title = "无法写入所选文件夹",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = $"HyPlayer 没有「{folder.Path}」的写入权限，{what}将无法保存到这里，请换一个文件夹。"
            },
            CloseButtonText = "知道了"
        };
        await dialog.ShowAsync();
        return false;
    }

    public static async Task CheckSavedFoldersAsync()
    {
        if (Interlocked.Exchange(ref _watching, 1) == 0)
            FolderAccess.AccessLost += OnAccessLost;
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            await Ioc.Default.GetRequiredService<DownloadSettings>().EnsureDefaultDownloadDirectoryAsync();
            await PromptLostFoldersAsync();
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    private static void OnAccessLost(string path)
    {
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>().Forget(
            Ioc.Default.GetRequiredService<IUIThreadDispatcher>().TryRunAsync(CheckSavedFoldersAsync),
            "check saved folder access");
    }

    private static async Task PromptLostFoldersAsync()
    {
        var lost = new List<string>();
        await AddIfLostAsync(lost, "缓存目录",
            Ioc.Default.GetRequiredService<PlaybackSettings>().CustomCacheDirectory);
        await AddIfLostAsync(lost, "下载目录",
            Ioc.Default.GetRequiredService<DownloadSettings>().CustomDownloadDirectory);
        await AddIfLostAsync(lost, "本地音乐搜索目录",
            Ioc.Default.GetRequiredService<LocalLibrarySettings>().CustomSearchDirectory);
        if (lost.Count == 0) return;

        await Task.Delay(DialogRetryDelay);
        var dialog = new ContentDialog
        {
            Title = "部分文件夹无法访问",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "以下自定义文件夹已失去访问权限，暂时改用默认位置：\n\n" + string.Join("\n", lost) +
                       "\n\n在 设置 → 存储设置 中重新选择一次这些文件夹即可恢复。"
            },
            PrimaryButtonText = "去设置",
            CloseButtonText = "稍后",
            DefaultButton = ContentDialogButton.Primary
        };
        for (var attempt = 0; attempt < 40; attempt++)
        {
            ContentDialogResult result;
            try
            {
                result = await dialog.ShowAsync();
            }
            catch
            {
                await Task.Delay(DialogRetryDelay);
                continue;
            }

            if (result == ContentDialogResult.Primary)
                await Ioc.Default.GetRequiredService<IAppNavigator>().NavigateAsync(new AppRoute.Settings());
            return;
        }
    }

    private static async Task<bool> CanWriteAsync(StorageFolder folder)
    {
        try
        {
            var probe = await folder.CreateFileAsync($".hyplayer_access_{Guid.NewGuid():N}.tmp",
                CreationCollisionOption.GenerateUniqueName);
            await probe.DeleteAsync(StorageDeleteOption.PermanentDelete);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task AddIfLostAsync(List<string> lost, string name, string? customPath)
    {
        if (customPath is null || await FolderAccess.ProbeAsync(customPath)) return;
        lost.Add($"• {name}：{customPath}");
    }
}

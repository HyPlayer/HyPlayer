#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Platform.Downloads;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.Storage.Cache;

namespace HyPlayer.Features.Settings.Services;

public static class StorageMigrationPrompt
{
    private static readonly TimeSpan RescanDelay = TimeSpan.FromSeconds(2);

    public static async Task<bool> OfferAsync(
        string what,
        string? oldPath,
        StorageFolder destination,
        StorageContentKind kind)
    {
        if (string.IsNullOrEmpty(oldPath) || FolderAccess.IsSamePath(oldPath, destination.Path)) return true;

        StorageFolder source;
        List<StorageItemInfo> items;
        try
        {
            source = await StorageFolder.GetFolderFromPathAsync(oldPath);
            items = await StorageMaintenance.CollectAsync(source, kind, destination.Path);
        }
        catch
        {
            return true;
        }

        if (items.Count == 0) return true;

        ulong totalBytes = 0;
        foreach (var item in items) totalBytes += item.Size;

        var notification = Ioc.Default.GetRequiredService<INotificationService>();
        var choice = await ShowAsync(new ContentDialog
        {
            Title = $"迁移{what}",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = $"原位置有 {items.Count} 个{what}文件（共 {StorageMaintenance.FormatSize(totalBytes)}），" +
                       $"是否迁移到新位置？\n\n{source.Path}\n→ {destination.Path}"
            },
            PrimaryButtonText = "迁移",
            SecondaryButtonText = "不迁移",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        });
        if (choice is null)
        {
            notification.ShowMessage("无法更改文件夹", "请先关闭其它对话框后再试一次");
            return false;
        }

        if (choice == ContentDialogResult.None) return false;
        if (choice == ContentDialogResult.Secondary) return true;

        var deleteOriginal = await ShowAsync(new ContentDialog
        {
            Title = "删除原位置的文件",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = $"迁移完成后，是否删除原位置的{what}文件？只有确认已复制到新位置的文件才会被删除。"
            },
            PrimaryButtonText = "删除",
            CloseButtonText = "保留",
            DefaultButton = ContentDialogButton.Close
        }) == ContentDialogResult.Primary;

        var result = await RunAsync(what, source, destination, items, deleteOriginal, notification);
        if (kind == StorageContentKind.Music && result.Moved > 0)
            DownloadedTrackIndex.ScheduleScan(RescanDelay);

        notification.ShowMessage(DescribeTitle(what, result), DescribeDetail(result, deleteOriginal));
        return true;
    }

    private static async Task<StorageMigrationResult> RunAsync(
        string what,
        StorageFolder source,
        StorageFolder destination,
        IReadOnlyList<StorageItemInfo> items,
        bool deleteOriginal,
        INotificationService notification)
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = items.Count };
        var status = new TextBlock { Text = $"0 / {items.Count}" };
        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "迁移完成前请不要关闭应用。停止后，尚未迁移的文件会保留在原位置。"
        });
        panel.Children.Add(bar);
        panel.Children.Add(status);

        using var cancellation = new CancellationTokenSource();
        var finished = false;
        var stopping = false;
        var dialog = new ContentDialog
        {
            Title = $"正在迁移{what}",
            Content = panel,
            CloseButtonText = "停止"
        };
        dialog.Closing += (_, args) =>
        {
            if (finished) return;
            args.Cancel = true;
            stopping = true;
            status.Text = "正在停止…";
            cancellation.Cancel();
        };

        var shown = false;
        try
        {
            _ = dialog.ShowAsync();
            shown = true;
        }
        catch
        {
            notification.ShowMessage($"正在迁移{what}", $"共 {items.Count} 个文件，迁移完成前请不要关闭应用");
        }

        var progress = new Progress<int>(done =>
        {
            bar.Value = done;
            if (!stopping) status.Text = $"{done} / {items.Count}";
        });
        var token = cancellation.Token;
        try
        {
            return await StorageMaintenance.MigrateAsync(source, destination, items, deleteOriginal, progress, token);
        }
        finally
        {
            finished = true;
            if (shown) dialog.Hide();
        }
    }

    private static string DescribeTitle(string what, StorageMigrationResult result)
    {
        if (result.Remaining > 0) return $"{what}迁移已停止";
        return result.Failed + result.Skipped > 0 ? $"{what}迁移部分完成" : $"{what}迁移完成";
    }

    private static string DescribeDetail(StorageMigrationResult result, bool deleteOriginal)
    {
        var detail = $"已迁移 {result.Moved} 个文件";
        if (result.Skipped > 0) detail += $"，{result.Skipped} 个因新位置已有同名文件而跳过";
        if (result.Failed > 0) detail += $"，{result.Failed} 个失败";
        if (result.Remaining > 0) detail += $"，{result.Remaining} 个尚未迁移";
        if (result.Skipped + result.Failed + result.Remaining > 0) detail += "，这些文件仍保留在原位置";
        if (!deleteOriginal) return detail + "；已迁移文件的原件已保留";
        return result.OriginalsKept > 0
            ? detail + $"；{result.OriginalsKept} 个原文件正在使用，未能删除"
            : detail + "；已迁移文件的原件已删除";
    }

    private static async Task<ContentDialogResult?> ShowAsync(ContentDialog dialog)
    {
        try
        {
            return await dialog.ShowAsync();
        }
        catch
        {
            return null;
        }
    }
}

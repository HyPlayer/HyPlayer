#nullable enable
using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Models;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.UWP.Chopin.Dsp;

namespace HyPlayer.UI.Dialogs;

public sealed partial class AutoMixSettingsDialog : ContentDialog
{
    private StorageFolder? _migrationTarget;
    private StorageFolder? _migrationSource;
    private StorageFolder? _migrationDestination;

    public AutoMixSettingsDialog()
    {
        InitializeComponent();
        if (!CanTimeStretch)
            TimeStretchCard.Description = "时间拉伸组件不可用，过渡时不会调整速度";
        if (Models.IsCustomFolder && !Models.IsCustomFolderAccessible)
            ShowStatus("自定义的分析模型路径已失去访问权限，暂时改用默认位置，重新选择一次即可恢复");
        _ = Models.RefreshAsync();
    }

    public AutoMixSettings Settings { get; } = Ioc.Default.GetRequiredService<AutoMixSettings>();

    public ModelManager Models { get; } = Ioc.Default.GetRequiredService<ModelManager>();

    public bool CanTimeStretch => TimeStretcher.IsAvailable;

    public bool CanGlide(bool timeStretch) => timeStretch && CanTimeStretch;

    private static ModelEntry? ModelOf(object sender)
    {
        return sender is FrameworkElement element ? element.Tag as ModelEntry ?? element.DataContext as ModelEntry : null;
    }

    private void ShowStatus(string? message)
    {
        StatusText.Text = message ?? string.Empty;
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ModelDownload_Click(object sender, RoutedEventArgs e)
    {
        if (ModelOf(sender) is not { } entry) return;
        ShowStatus(null);
        await Models.DownloadAsync(entry);
    }

    private void ModelCancel_Click(object sender, RoutedEventArgs e)
    {
        if (ModelOf(sender) is { } entry) Models.CancelDownload(entry);
    }

    private async void ModelDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ModelOf(sender) is not { } entry) return;
        try
        {
            await Models.DeleteAsync(entry);
        }
        catch (Exception ex)
        {
            ShowStatus($"删除模型失败：{ex.Message}");
        }
    }

    private async void ModelImport_Click(object sender, RoutedEventArgs e)
    {
        if (ModelOf(sender) is not { } entry) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(".onnx");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var error = await Models.ImportAsync(entry, file);
        ShowStatus(error is null ? null : $"导入模型失败：{error}");
    }

    private async void ModelFolderSelect_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) await ChangeModelFolderAsync(folder);
    }

    private async void ModelFolderReset_Click(object sender, RoutedEventArgs e)
    {
        await ChangeModelFolderAsync(null);
    }

    private async Task ChangeModelFolderAsync(StorageFolder? target)
    {
        ShowStatus(null);
        try
        {
            var current = await Models.GetFolderAsync();
            var destination = target ?? await ModelManager.GetDefaultFolderAsync();
            if (string.Equals(current.Path, destination.Path, StringComparison.OrdinalIgnoreCase))
            {
                await Models.SetFolderAsync(target);
                return;
            }

            var files = await ModelManager.GetModelFilesAsync(current);
            if (files.Count == 0)
            {
                await Models.SetFolderAsync(target);
                return;
            }

            ulong totalBytes = 0;
            foreach (var file in files) totalBytes += (await file.GetBasicPropertiesAsync()).Size;
            _migrationTarget = target;
            _migrationSource = current;
            _migrationDestination = destination;
            MigrationText.Text =
                $"原位置有 {files.Count} 个分析模型文件（共 {StorageMaintenance.FormatSize(totalBytes)}），是否迁移到新位置？\n{current.Path}\n→ {destination.Path}";
            SetMigrationPending(true);
        }
        catch (Exception ex)
        {
            ShowStatus($"无法更改模型位置：{ex.Message}");
        }
    }

    private void SetMigrationPending(bool pending)
    {
        MigrationPanel.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        MigrationButtons.Visibility = Visibility.Visible;
        ModelFolderSelectButton.IsEnabled = !pending;
        ModelFolderResetButton.IsEnabled = !pending;
        if (pending) return;
        _migrationTarget = null;
        _migrationSource = null;
        _migrationDestination = null;
    }

    private async Task CompleteMigrationAsync(bool migrate, bool deleteOriginal)
    {
        var target = _migrationTarget;
        var source = _migrationSource;
        var destination = _migrationDestination;
        if (source is null || destination is null)
        {
            SetMigrationPending(false);
            return;
        }

        try
        {
            if (migrate)
            {
                MigrationButtons.Visibility = Visibility.Collapsed;
                MigrationText.Text = "正在迁移分析模型，迁移完成前请不要关闭应用";
                await Models.MigrateAsync(source, destination, deleteOriginal);
            }

            await Models.SetFolderAsync(target);
            ShowStatus(!migrate ? null : deleteOriginal ? "已迁移并删除原位置的模型文件" : "已迁移，原位置的模型文件已保留");
        }
        catch (Exception ex)
        {
            ShowStatus($"迁移失败，模型位置未更改：{ex.Message}");
        }
        finally
        {
            SetMigrationPending(false);
        }
    }

    private async void MigrationMove_Click(object sender, RoutedEventArgs e)
    {
        await CompleteMigrationAsync(true, true);
    }

    private async void MigrationCopy_Click(object sender, RoutedEventArgs e)
    {
        await CompleteMigrationAsync(true, false);
    }

    private async void MigrationSkip_Click(object sender, RoutedEventArgs e)
    {
        await CompleteMigrationAsync(false, false);
    }

    private void MigrationCancel_Click(object sender, RoutedEventArgs e)
    {
        SetMigrationPending(false);
    }
}

using System;
using WinRT;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Collections.Generic;
using System.Threading;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.UI.Lists;

namespace HyPlayer.Features.Library;

public sealed partial class MusicCloudPage : Page
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IBackgroundTaskRunner _taskRunner =
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();

    public MusicCloudViewModel ViewModel { get; } =
        Ioc.Default.GetRequiredService<MusicCloudViewModel>();

    public MusicCloudPage()
    {
        ItemActions =
        [
            new ProvidableItemAction
            {
                Text = "从云盘删除",
                ExecuteAsync = row => ViewModel.DeleteItemAsync(row, _cancellationTokenSource.Token)
            }
        ];
        InitializeComponent();
    }

    public List<ProvidableItemAction> ItemActions { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _taskRunner.Forget(
            ViewModel.LoadAsync(_cancellationTokenSource.Token),
            "load cloud music page");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        SongContainer.ReleaseResources();
        Bindings.StopTracking();
    }

    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        SongContainer.DownloadAllLoaded();
    }

    private async void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".ncm");
        picker.FileTypeFilter.Add(".ape");
        picker.FileTypeFilter.Add(".m4a");
        picker.FileTypeFilter.Add(".wav");

        var files = await picker.PickMultipleFilesAsync();
        if (files is { Count: > 0 })
            await ViewModel.UploadAsync(files);
    }

    private async void BtnRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync(_cancellationTokenSource.Token);
    }
}

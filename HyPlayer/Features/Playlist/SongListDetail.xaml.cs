#region

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Platform.Runtime.Background;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.UI.Lists;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了"空白页"项模板

namespace HyPlayer.Features.Playlist;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class SongListDetail : Page
{

    private readonly IContainerItemManagementProvidable _containerItemManagement =
        Ioc.Default.GetRequiredService<IContainerItemManagementProvidable>();

    private readonly DataTransferManager _dataTransferManager = DataTransferManager.GetForCurrentView();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IBackgroundTaskRunner _taskRunner =
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    public SongListViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<SongListViewModel>();

    private bool _dataRequestedSubscribed;

    public SongListDetail()
    {
        ItemActions =
        [
            new ProvidableItemAction
            {
                Text = "从歌单移除",
                CanExecute = _ => ViewModel.IsMySongList && ViewModel.PlayList is not null,
                ExecuteAsync = RemoveItemFromPlaylistAsync
            }
        ];
        InitializeComponent();
        Unloaded += SongListDetail_Unloaded;
        AttachDataRequested();
    }

    public List<ProvidableItemAction> ItemActions { get; }

    private async Task RemoveItemFromPlaylistAsync(ProvidableItemRowViewModel row)
    {
        if (ViewModel.PlayList is null || string.IsNullOrWhiteSpace(row.ActualId))
            return;

        try
        {
            await _containerItemManagement.RemoveItemFromContainerAsync(ViewModel.PlayList.ActualId, row.ActualId);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("移除失败", ex.Message);
            return;
        }

        _notification.ShowMessage("已从歌单移除", row.Title);
        try
        {
            if (!await ViewModel.ReloadFromProviderAsync())
                _notification.ShowMessage("刷新歌单失败", "服务端未返回歌单信息");
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("歌曲已移除，但刷新歌单失败", ex.Message);
        }
    }

    private void AttachDataRequested()
    {
        if (_dataRequestedSubscribed)
            return;

        _dataTransferManager.DataRequested += DataTransferManagerOnDataRequested;
        _dataRequestedSubscribed = true;
    }

    private void DetachDataRequested()
    {
        if (!_dataRequestedSubscribed)
            return;

        _dataTransferManager.DataRequested -= DataTransferManagerOnDataRequested;
        _dataRequestedSubscribed = false;
    }

    private void DataTransferManagerOnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var dp = new DataPackage();
        dp.Properties.Title = ViewModel.PlayList?.Name;
        dp.SetWebLink(new Uri("https://music.163.com/#/playlist?id=" + ViewModel.PlayList?.ActualId));
        var request = args.Request;
        request.Data = dp;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        DetachDataRequested();
        ContainerSongs.ReleaseResources();
        Bindings.StopTracking();
    }

    private void SongListDetail_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachDataRequested();
        ViewModel.Dispose();
        Unloaded -= SongListDetail_Unloaded;
    }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        switch (e.Parameter)
        {
            case ContainerBase playlist:
                _taskRunner.Forget(ViewModel.LoadAsync(playlist), "load playlist page");
                break;
            case string playlistId when !string.IsNullOrWhiteSpace(playlistId):
                _taskRunner.Forget(ViewModel.LoadAsync(playlistId), "load playlist page");
                break;
        }
    }

    private void BtnShare_Clicked(object sender, RoutedEventArgs e)
    {
        DataTransferManager.ShowShareUI();
    }

    private async void PlayAll_Click(object sender, RoutedEventArgs e)
    {
        await ContainerSongs.PlayAllAsync();
    }

    private async void AddAll_Click(object sender, RoutedEventArgs e)
    {
        await ContainerSongs.AddAllToPlaylistAsync();
    }

    private void DownloadAll_Click(object sender, RoutedEventArgs e)
    {
        ContainerSongs.DownloadAllLoaded();
    }

    private async void ResetCache_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ResetCacheAsync();
    }
}

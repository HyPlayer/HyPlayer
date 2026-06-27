#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction.Models;
using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了"空白页"项模板

namespace HyPlayer.Features.Playlist;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class SongListDetail : Page
{
    private const string DailyRecommendPlaylistId = "daily_recommend";
    private DataTransferManager _dataTransferManager = DataTransferManager.GetForCurrentView();
    private bool _dataRequestedSubscribed;
    public SongListViewModel ViewModel => (SongListViewModel)DataContext;

    public SongListDetail()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<SongListViewModel>();
        Unloaded += SongListDetail_Unloaded;
        AttachDataRequested();
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

        if (e.Parameter != null)
        {
            if (e.Parameter is ContainerBase playList)
            {
                var isDailyRecommend = playList.ActualId == DailyRecommendPlaylistId;
                ViewModel.IsDailyRecommend = isDailyRecommend;
                if (isDailyRecommend)
                {
                    ViewModel.PlayList = playList;
                    ViewModel.DescriptionBoxContent = string.Empty;
                    ViewModel.LoadSongListItem().SafeFireAndForget();
                }
                else
                {
                    ViewModel.LoadPageData(playList.ActualId, true).SafeFireAndForget();
                }
            }
            else
            {
                var pid = e.Parameter.ToString();
                ViewModel.IsDailyRecommend = false;
                ViewModel.LoadPageData(pid, true).SafeFireAndForget();
            }
        }
    }

    private void BtnShare_Clicked(object sender, RoutedEventArgs e)
    {
        DataTransferManager.ShowShareUI();
    }
}

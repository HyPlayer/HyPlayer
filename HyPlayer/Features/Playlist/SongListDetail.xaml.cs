#region
using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using HyPlayer.ViewModels;
using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class SongListDetail : Page
{
    private DataTransferManager _dataTransferManager = DataTransferManager.GetForCurrentView();
    public SongListViewModel ViewModel => (SongListViewModel)DataContext;

    public SongListDetail()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<SongListViewModel>();
        _dataTransferManager.DataRequested += DataTransferManagerOnDataRequested;
    }

    private void DataTransferManagerOnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var dp = new DataPackage();
        dp.Properties.Title = ViewModel.PlayList.Name;
        dp.SetWebLink(new Uri("https://music.163.com/#/playlist?id=" + ViewModel.PlayList.PlaylistId));
        var request = args.Request;
        request.Data = dp;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _dataTransferManager.DataRequested -= DataTransferManagerOnDataRequested;
    }



    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter != null)
        {
            if (e.Parameter is NCPlayList playList)
            {
                ViewModel.PlayList = playList;
                ViewModel.LoadPageData(playList.PlaylistId, false).SafeFireAndForget();
            }
            else
            {
                var pid = e.Parameter.ToString();
                ViewModel.LoadPageData(pid, true).SafeFireAndForget();
            }
        }
    }

    private void BtnShare_Clicked(object sender, RoutedEventArgs e)
    {
        DataTransferManager.ShowShareUI();
    }
}
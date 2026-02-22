#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.ViewModels;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class ArtistPage : Page
{

    public ArtistPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<ArtistPageViewModel>();
    }
    private ArtistPageViewModel ViewModel => (ArtistPageViewModel)DataContext;
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var artistId = e.Parameter as string;
        if (artistId is null)
        {
            Common.AddToTeachingTipLists("艺人ID为空", "请检查传入的参数是否正确");
            return;
        }
        ViewModel.InitializeArtistInfo(artistId).SafeFireAndForget();
    }

    private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.CurrentPage = 0;
    }
}
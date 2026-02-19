#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.ViewModels;
using System;
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

    private void PivotView_HeaderScrollProgressChanged(object sender, EventArgs e)
    {
        GridPersonalInformation.Opacity = 1 - PivotView.HeaderScrollProgress * 1.4;
        RectangleImageBack.Opacity = 1 - PivotView.HeaderScrollProgress * 1.1;
        RectangleImageBackAcrylic.Opacity = 1 - PivotView.HeaderScrollProgress * 1.1;
        TextBlockDesc.Opacity = 1 - PivotView.HeaderScrollProgress * 0.8;

        UserScale.ScaleX = 1 - PivotView.HeaderScrollProgress * 0.8;
        UserScale.ScaleY = 1 - PivotView.HeaderScrollProgress * 0.8;
        UserInfoScale.ScaleX = 1 - PivotView.HeaderScrollProgress * 0.6;
        UserInfoScale.ScaleY = 1 - PivotView.HeaderScrollProgress * 0.6;
        DescScale.ScaleX = 1 - PivotView.HeaderScrollProgress * 0.4;
        DescScale.ScaleY = 1 - PivotView.HeaderScrollProgress * 0.4;
    }
}
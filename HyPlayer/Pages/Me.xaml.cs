#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.ViewModels;
using Microsoft.UI.Xaml.Controls;
using System.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Me : Page
{
    public Me()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<MeViewModel>();
        Unloaded += Me_Unloaded;
    }
    private MeViewModel ViewModel => (MeViewModel)DataContext;
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter != null)
        {
            ViewModel.InitializeUserInfo((string)e.Parameter).SafeFireAndForget();
            ButtonLogout.Visibility = Visibility.Collapsed;
        }
        else
        {
            ViewModel.InitializeUserInfo(Common.LoginedUser.Id).SafeFireAndForget();
        }
    }

    private void Logout_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Common.Logined = false;
            Common.LoginedUser = new NCUser();
            if (ApplicationData.Current.LocalSettings.Containers.TryGetValue("Cookies", out var container))
            {
                container.Values.Clear();
            }
            Common.NeteaseAPI.Option.Cookies.Clear();
            Common.Setting.SaveCookies();
            Common.PageMain.MainFrame.Navigate(typeof(BasePage));
            _ = SimpleCacher.ClearCacheAsync(CacheType.Login);
            _ = ((App)Application.Current).InitializeJumpList();
        }
        catch
        {
        }
    }

    private void RectangleImage_OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        Common.Setting.IsOldThemeEnabled = false;
        Common.AddToTeachingTipLists("已重置, 请重启");
    }

    private void Me_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LikedPlaylist.Clear();
        ViewModel.MyPlaylist.Clear();
        ViewModel.LikedPlaylist = null;
        ViewModel.MyPlaylist = null;
        ClearItemsRepeater(MySongListBox);
        ClearItemsRepeater(LikedSongList);
        ClearImageBrush(RectangleImageBack);
        ClearImageBrush(RectangleImageFallback);
        AcrylicGrid.Children.Clear();
        UserAvatar.ProfilePicture = null;
        ButtonLogout.Click -= Logout_OnClick;
        DataContext = null;
    }


    private void ClearItemsRepeater(ItemsRepeater repeater)
    {
        if (repeater == null) return;
        repeater.ItemsSource = null;
        repeater.Layout = null;
        repeater.UpdateLayout();
    }

    private void ClearImageBrush(Border border)
    {
        if (border?.Background is ImageBrush brush)
        {
            brush.ImageSource = null;
            border.Background = null;
        }
    }
}

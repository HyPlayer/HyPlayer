#region
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Services.Abstractions;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRT;

#endregion

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace HyPlayer.Pages;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class Welcome : Page
{
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();

    public Welcome()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_auth.IsLoggedIn)
            TBHINT.Text = "点击侧边按钮开始吧~";
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ImageE.Source = null;
    }

    private async void LoginBtn_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
    {
        var frame = Window.Current.Content?.As<Frame>();
        var mainpage = frame.Content?.As<MainPage>();
        var content = mainpage.MainFrame.Content?.As<BasePage>();
        await content.DialogPreLoginHint.ShowAsync();
    }
}
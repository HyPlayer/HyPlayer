#region

using System;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Account.Services;
using HyPlayer.Platform.Xaml;
using HyPlayer.Shell.Login;
using HyPlayer.Shell.Navigation;

#endregion

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace HyPlayer.Features.Welcome;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class Welcome : Page
{
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();
    private readonly ShellLoginService _loginService = Ioc.Default.GetRequiredService<ShellLoginService>();

    private readonly NavigationShellViewModel _navigationShell =
        Ioc.Default.GetRequiredService<NavigationShellViewModel>();

    public Welcome()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _auth.LoginCompleted += Auth_LoginCompleted;
        _navigationShell.PropertyChanged += NavigationShell_PropertyChanged;
        ApplyLoginState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _auth.LoginCompleted -= Auth_LoginCompleted;
        _navigationShell.PropertyChanged -= NavigationShell_PropertyChanged;
        LoginPendingRing.IsActive = false;
        ImageE.Source = null;
    }

    private void Auth_LoginCompleted(object sender, EventArgs e)
    {
        _ = this.RunOnUIThreadAsync(ApplyLoginState);
    }

    private void NavigationShell_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationShellViewModel.AutoLoginState))
            ApplyLoginState();
    }

    private void ApplyLoginState()
    {
        var state = _navigationShell.AutoLoginState;
        var isLoggedIn = _auth.IsLoggedIn;
        TBHINT.Text = isLoggedIn ? "点击侧边按钮开始吧~" : "欢迎使用 HyPlayer!";
        TBSettingsHint.Text = state switch
        {
            _ when isLoggedIn => "推荐您进入设置进行个性化",
            AutoLoginState.Pending => "正在连接至网易云，网络较慢时可能需要一些时间，请稍候",
            AutoLoginState.Unreachable => "自动登录未完成，可能是网络较慢或登录已失效。网络恢复后会自动重试，也可以点击「重试」",
            _ => "推荐您在登录后进入设置进行个性化"
        };
        LoginBtn.Visibility = !isLoggedIn && state == AutoLoginState.None
            ? Visibility.Visible
            : Visibility.Collapsed;
        LoginPendingRing.IsActive = state == AutoLoginState.Pending;
        LoginPendingPanel.Visibility = state == AutoLoginState.Pending ? Visibility.Visible : Visibility.Collapsed;
        LoginRetryPanel.Visibility = state == AutoLoginState.Unreachable ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void LoginBtn_Click(object sender, RoutedEventArgs e)
    {
        await _loginService.ShowLoginRequiredDialogAsync();
    }

    private async void RetryBtn_Click(object sender, RoutedEventArgs e)
    {
        await _loginService.RetrySavedLoginAsync();
    }

    private async void ReloginBtn_Click(object sender, RoutedEventArgs e)
    {
        await _loginService.ShowReloginDialogAsync();
    }
}
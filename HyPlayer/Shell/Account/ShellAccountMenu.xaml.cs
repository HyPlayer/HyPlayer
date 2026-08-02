using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Navigation;
using HyPlayer.Features.Account.Services;
using HyPlayer.Shell.Login;
using HyPlayer.Shell.Navigation;
using HyPlayer.Shell.Navigation.Services;

namespace HyPlayer.Shell.Account;

public sealed partial class ShellAccountMenu : UserControl
{
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();
    private readonly ShellLoginService _loginService = Ioc.Default.GetRequiredService<ShellLoginService>();

    private readonly NavigationShellViewModel _navigationShell =
        Ioc.Default.GetRequiredService<NavigationShellViewModel>();

    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();

    public ShellAccountMenu()
    {
        InitializeComponent();
    }

    private async void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_auth.IsLoggedIn)
        {
            await _loginService.ShowLoginRequiredDialogAsync();
            return;
        }

        await _navigator.NavigateAsync(new AppRoute.Me());
    }

    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        await _navigator.NavigateAsync(new AppRoute.Settings());
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        await _loginService.LogoutAsync();
    }
}
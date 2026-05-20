using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Navigation;
using HyPlayer.Services.Abstractions;
using HyPlayer.Shell.Login;
using HyPlayer.Shell.Navigation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Shell.Account;

public sealed partial class ShellAccountMenu : UserControl
{
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();
    private readonly ShellLoginService _loginService = Ioc.Default.GetRequiredService<ShellLoginService>();
    private readonly NavigationShellViewModel _navigationShell = Ioc.Default.GetRequiredService<NavigationShellViewModel>();

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

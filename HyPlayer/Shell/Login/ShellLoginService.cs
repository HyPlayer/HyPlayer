using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Welcome;
using HyPlayer.Shell.Navigation;
using HyPlayer.Shell.Navigation.Services;
using QRCoder;
using InfoBarSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity;

namespace HyPlayer.Shell.Login;

/// <summary>
///     Coordinates shell login UI while AuthService owns authentication state and API calls.
/// </summary>
public sealed class ShellLoginService
{
    private readonly IAuthService _auth;
    private readonly INavigationService _navigation;
    private readonly NavigationShellViewModel _navigationShell;
    private readonly IAppNavigator _navigator;
    private readonly INotificationService _notification;
    private readonly Setting _setting;

    private ContentDialog? _currentLoginDialog;
    private ContentDialog? _currentPreLoginDialog;

    public ShellLoginService(
        IAuthService auth,
        INotificationService notification,
        INavigationService navigation,
        IAppNavigator navigator,
        NavigationShellViewModel navigationShell,
        Setting setting)
    {
        _auth = auth;
        _notification = notification;
        _navigation = navigation;
        _navigator = navigator;
        _navigationShell = navigationShell;
        _setting = setting;
    }

    public event EventHandler<QrLoginStatusChangedEventArgs>? QrLoginStatusChanged;

    public async Task ShowLoginRequiredDialogAsync()
    {
        await _auth.ClearRuntimeCookiesAsync();
        await ShowPreLoginHintAsync();
    }

    public async Task TryLoadSavedLoginAsync()
    {
        var result = await _auth.TryLoadSavedLoginAsync();
        if (!result.IsSuccess)
        {
            if (!string.IsNullOrEmpty(result.ErrorMessage))
                _notification.ShowMessage("自动登录失败", result.ErrorMessage);
            _navigation.Navigate(typeof(Welcome));
            return;
        }

        await FinishSuccessfulLoginAsync();
    }

    public async Task LogoutAsync()
    {
        var result = await _auth.LogoutAsync();
        if (!result.IsSuccess && !string.IsNullOrEmpty(result.ErrorMessage))
            _notification.ShowMessage("清除登录缓存失败", result.ErrorMessage);

        _navigationShell.UpdateAfterLogout();
        _navigation.Navigate(typeof(Welcome));
    }

    private async Task ShowPreLoginHintAsync()
    {
        var dialog = CreatePreLoginHintDialog();
        _currentPreLoginDialog = dialog;
        dialog.Closed += (_, _) => ClearCurrentPreLoginDialog(dialog);
        await dialog.ShowAsync();
    }

    public async Task ShowLoginDialogAsync()
    {
        var dialog = new LoginDialog(this);
        _currentLoginDialog = dialog;
        dialog.Closed += (_, _) => ClearCurrentLoginDialog(dialog);
        await dialog.ShowAsync();
    }

    public async Task<AuthResult> LoginWithPasswordAsync(string account, string password)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            return new AuthResult(false, "用户名或密码不能为空");

        var result = await _auth.LoginWithPasswordAsync(account, password);
        if (result.IsSuccess) await FinishSuccessfulLoginAsync();

        return result;
    }

    public async Task StartQrLoginAsync(
        Guid statusSessionId,
        Func<string, Task> refreshQrImage,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = await _auth.CreateQrLoginKeyAsync();
            if (!key.IsSuccess || key.Key is null)
            {
                SendQrLoginStatus(statusSessionId, "获取UniKey失败: " + key.ErrorMessage, InfoBarSeverity.Error);
                return;
            }

            var activeKey = key.Key;
            await refreshQrImage(activeKey);
            cancellationToken.ThrowIfCancellationRequested();
            while (!_auth.IsLoggedIn && !cancellationToken.IsCancellationRequested)
            {
                var res = await _auth.CheckQrLoginAsync(activeKey);
                cancellationToken.ThrowIfCancellationRequested();
                if (res.Code == 800)
                {
                    key = await _auth.CreateQrLoginKeyAsync();
                    if (!key.IsSuccess || key.Key is null)
                    {
                        SendQrLoginStatus(statusSessionId, "获取UniKey失败: " + key.ErrorMessage, InfoBarSeverity.Error);
                        return;
                    }

                    activeKey = key.Key;
                    await refreshQrImage(activeKey);
                    cancellationToken.ThrowIfCancellationRequested();
                    SendQrLoginStatus(statusSessionId, "请扫描上方二维码登录");
                }
                else if (res.Code == 801)
                {
                    SendQrLoginStatus(statusSessionId, "请扫描上方二维码登录");
                }
                else if (res.Code == 803)
                {
                    SendQrLoginStatus(statusSessionId, "登录成功", InfoBarSeverity.Success);
                    await CompleteExternalLoginAsync();
                    break;
                }
                else if (res.Code == 802)
                {
                    SendQrLoginStatus(statusSessionId, "请在手机上授权登录");
                }
                else
                {
                    SendQrLoginStatus(statusSessionId,
                        string.IsNullOrEmpty(res.ErrorMessage)
                            ? "正在刷新扫码状态"
                            : "检查二维码登录状态失败: " + res.ErrorMessage,
                        string.IsNullOrEmpty(res.ErrorMessage)
                            ? InfoBarSeverity.Informational
                            : InfoBarSeverity.Error);
                }

                await Task.Delay(2000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SendQrLoginStatus(statusSessionId, "扫码登录发生错误: " + ex.Message, InfoBarSeverity.Error);
        }
    }

    private void SendQrLoginStatus(Guid sessionId, string title,
        InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        QrLoginStatusChanged?.Invoke(this, new QrLoginStatusChangedEventArgs(sessionId, title, severity));
    }

    public static async Task<BitmapImage> GenerateQrImageAsync(string key)
    {
        var qrUri = new Uri("https://music.163.com/login?codekey=" + key);
        var img = new BitmapImage();
        var qrGenerator = new QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(qrUri.ToString(), QRCodeGenerator.ECCLevel.M);
        var qrCode = new BitmapByteQRCode(qrData);
        var qrImage = qrCode.GetGraphic(20);
        using (var stream = new InMemoryRandomAccessStream())
        {
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(qrImage);
                await writer.StoreAsync();
            }

            await img.SetSourceAsync(stream);
        }

        return img;
    }

    public void NavigateToThirdPartyLogin(string provider)
    {
        _currentLoginDialog?.Hide();
        _navigation.Navigate(typeof(ThirdPartyLogin), provider);
    }

    private void ClearCurrentLoginDialog(ContentDialog dialog)
    {
        if (ReferenceEquals(_currentLoginDialog, dialog))
            _currentLoginDialog = null;
    }

    private void ClearCurrentPreLoginDialog(ContentDialog dialog)
    {
        if (ReferenceEquals(_currentPreLoginDialog, dialog))
            _currentPreLoginDialog = null;
    }

    public async Task RegisterDeviceAndLoginAsync(ContentDialog dialog)
    {
        var result = await _auth.RegisterCurrentDeviceAsync();
        if (!result.IsSuccess)
        {
            _notification.ShowMessage("设备ID注册失败, 请尝试其他方案", "获取失败: " + result.ErrorMessage);
            return;
        }

        _notification.ShowMessage("设备ID注册成功", "临时用户 ID: " + result.TemporaryUserId);
        dialog.Hide();
        await ShowLoginDialogAsync();
    }

    public async Task CompleteExternalLoginAsync()
    {
        var result = await _auth.CompleteLoginAsync(true);
        if (!result.IsSuccess)
        {
            _notification.ShowMessage("登录失败", result.ErrorMessage);
            return;
        }

        await FinishSuccessfulLoginAsync();
    }

    private async Task FinishSuccessfulLoginAsync()
    {
        _navigationShell.UpdateAfterLogin();
        await _navigationShell.RefreshPlaylistsAsync();
        _currentLoginDialog?.Hide();
        _currentPreLoginDialog?.Hide();

        if (_setting.noImage)
            _navigation.Navigate(typeof(Welcome));
        else
            await _navigator.NavigateAsync(new AppRoute.Me());
    }

    private PreLoginHintDialog CreatePreLoginHintDialog()
    {
        var dialog = new PreLoginHintDialog();
        dialog.TutorialRequested += () =>
        {
            _ = Launcher.LaunchUriAsync(
                new Uri("https://github.com/HyPlayer/HyPlayer/wiki/%E5%85%B3%E4%BA%8E-%60ApiAdditionalParameter%60"));
            _navigation.Navigate(typeof(TestPage));
        };
        dialog.RegisterDeviceRequested += async () => { await RegisterDeviceAndLoginAsync(dialog); };
        dialog.PrimaryButtonClick += (_, _) =>
        {
            dialog.Hide();
            _ = ShowLoginDialogAsync();
        };
        return dialog;
    }
}
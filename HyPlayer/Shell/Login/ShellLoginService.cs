using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.Threading;
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
    private static readonly TimeSpan PendingPageDelay = TimeSpan.FromMilliseconds(800);

    private readonly IAuthService _auth;
    private readonly AutoLoginFlow _autoLogin;
    private readonly INavigationService _navigation;
    private readonly NavigationShellViewModel _navigationShell;
    private readonly NetworkStatusChangedEventHandler _networkStatusChanged;
    private readonly IAppNavigator _navigator;
    private readonly INotificationService _notification;
    private readonly UISettings _setting;
    private readonly IUIThreadDispatcher _uiThreadDispatcher;

    private ContentDialog? _currentLoginDialog;
    private ContentDialog? _currentPreLoginDialog;
    private bool _watchingNetwork;

    public ShellLoginService(
        IAuthService auth,
        INotificationService notification,
        INavigationService navigation,
        IAppNavigator navigator,
        NavigationShellViewModel navigationShell,
        UISettings setting,
        IUIThreadDispatcher uiThreadDispatcher)
    {
        _auth = auth;
        _notification = notification;
        _navigation = navigation;
        _navigator = navigator;
        _navigationShell = navigationShell;
        _setting = setting;
        _uiThreadDispatcher = uiThreadDispatcher;
        _networkStatusChanged = OnNetworkStatusChanged;
        _autoLogin = new AutoLoginFlow(token => _auth.TryLoadSavedLoginAsync(token), Task.Delay);
        _autoLogin.StateChanged += OnAutoLoginStateChanged;
        _autoLogin.SlowDetected += OnAutoLoginSlow;
        _autoLogin.AttemptCompleted += OnAutoLoginAttemptCompleted;
    }

    public event EventHandler<QrLoginStatusChangedEventArgs>? QrLoginStatusChanged;

    public async Task ShowLoginRequiredDialogAsync()
    {
        if (_navigationShell.IsAutoLoginUnreachable)
        {
            await RetrySavedLoginAsync();
            return;
        }

        await ShowReloginDialogAsync();
    }

    public async Task ShowReloginDialogAsync()
    {
        if (_navigationShell.IsAutoLoginPending)
        {
            _notification.ShowMessage("正在登录", "正在连接至网易云，请稍候");
            return;
        }

        _autoLogin.Stop();
        await _auth.ClearRuntimeCookiesAsync();
        await ShowPreLoginHintAsync();
    }

    public Task RetrySavedLoginAsync()
    {
        if (_navigationShell.IsAutoLoginPending)
        {
            _notification.ShowMessage("正在登录", "正在连接至网易云，请稍候");
            return Task.CompletedTask;
        }

        return _autoLogin.RetryAsync();
    }

    public async Task TryLoadSavedLoginAsync()
    {
        var attempt = _autoLogin.StartAsync();
        if (!attempt.IsCompleted)
            await Task.WhenAny(attempt, Task.Delay(PendingPageDelay));
        if (_navigationShell.AutoLoginState != AutoLoginState.None)
            ShowWelcomeWhenEmpty();
        await attempt;
    }

    private void OnAutoLoginStateChanged(AutoLoginState state)
    {
        _navigationShell.SetAutoLoginState(state);
        WatchNetwork(state != AutoLoginState.None);
    }

    private void OnAutoLoginSlow()
    {
        if (!_auth.IsLoggedIn)
            _notification.ShowMessage("正在登录", "网络较慢，仍在连接至网易云，请稍候");
    }

    private void OnAutoLoginAttemptCompleted(SavedLoginResult result, AutoLoginTrigger trigger)
    {
        if (result.Status == SavedLoginStatus.Succeeded)
        {
            _ = FinishAutoLoginAsync();
            return;
        }

        if (_auth.IsLoggedIn)
        {
            _autoLogin.Stop();
            ShowWelcomeWhenEmpty();
            return;
        }

        switch (result.Status)
        {
            case SavedLoginStatus.Unreachable when trigger != AutoLoginTrigger.Automatic:
                _notification.ShowMessage("自动登录未完成",
                    "可能是网络较慢或登录已失效，网络恢复后会自动重试，也可点击头像菜单中的「重试登录」");
                break;
            case SavedLoginStatus.Rejected:
                _notification.ShowMessage("自动登录失败", "登录已失效，请重新登录");
                break;
        }

        ShowWelcomeWhenEmpty();
    }

    private void ShowWelcomeWhenEmpty()
    {
        if (_navigation.RootFrame is { Content: null })
            _navigation.Navigate(typeof(Welcome));
    }

    private void WatchNetwork(bool watch)
    {
        if (_watchingNetwork == watch) return;

        _watchingNetwork = watch;
        if (watch)
            NetworkInformation.NetworkStatusChanged += _networkStatusChanged;
        else
            NetworkInformation.NetworkStatusChanged -= _networkStatusChanged;
    }

    private void OnNetworkStatusChanged(object sender)
    {
        if (!HasInternetAccess()) return;

        _ = _uiThreadDispatcher.TryRunAsync(_autoLogin.NotifyNetworkAvailable);
    }

    private static bool HasInternetAccess()
    {
        try
        {
            return NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() ==
                   NetworkConnectivityLevel.InternetAccess;
        }
        catch
        {
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        _autoLogin.Stop();
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
        _autoLogin.Stop();
        await ApplySuccessfulLoginAsync();
        await NavigateAfterLoginAsync();
    }

    private async Task FinishAutoLoginAsync()
    {
        await ApplySuccessfulLoginAsync();
        var frame = _navigation.RootFrame;
        if (frame is null) return;

        if (frame.Content is not (null or Welcome))
        {
            _notification.ShowMessage("登录成功", "已恢复登录状态");
            return;
        }

        var replacesWelcome = frame.Content is Welcome;
        await NavigateAfterLoginAsync();
        if (replacesWelcome && frame.Content is not Welcome && frame.BackStack.Count > 0 &&
            frame.BackStack[^1].SourcePageType == typeof(Welcome))
            frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
    }

    private async Task ApplySuccessfulLoginAsync()
    {
        _navigationShell.UpdateAfterLogin();
        await _navigationShell.RefreshPlaylistsAsync();
        _currentLoginDialog?.Hide();
        _currentPreLoginDialog?.Hide();
    }

    private async Task NavigateAfterLoginAsync()
    {
        if (_setting.NoImage)
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
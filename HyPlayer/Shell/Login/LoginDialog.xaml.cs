using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using WinRT;
using InfoBarSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity;

namespace HyPlayer.Shell.Login;

public sealed partial class LoginDialog : ContentDialog
{
    private readonly ShellLoginService _loginService;
    private CancellationTokenSource? _qrLoginCts;
    private Guid? _qrLoginStatusSessionId;
    private bool _isPasswordLoginRunning;

    public LoginDialog(ShellLoginService loginService)
    {
        _loginService = loginService;
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<QrLoginStatusMessage>(this,
            (recipient, message) => ((LoginDialog)recipient).UpdateQrLoginStatus(message));

        PrimaryButtonClick += OnPrimaryButtonClick;
        Closed += OnDialogClosed;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        args.Cancel = true;
        try
        {
            await SubmitPasswordLoginAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        StopQrLoginPolling();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private void TextBoxAccount_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) TextBoxPassword.Focus(FocusState.Keyboard);
    }

    private void TextBoxPassword_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) _ = SubmitPasswordLoginAsync();
    }

    private async Task SubmitPasswordLoginAsync()
    {
        if (_isPasswordLoginRunning) return;

        _isPasswordLoginRunning = true;
        IsPrimaryButtonEnabled = false;
        PrimaryButtonText = "登录中......";
        try
        {
            var result = await _loginService.LoginWithPasswordAsync(TextBoxAccount.Text, TextBoxPassword.Password);
            if (result.IsSuccess) return;

            InfoBarLoginHint.IsOpen = true;
            InfoBarLoginHint.Title = "登录失败";
            InfoBarLoginHint.Severity = InfoBarSeverity.Warning;
            InfoBarLoginHint.Message = result.ErrorMessage is null
                ? "登录失败"
                : "登录失败 " + result.ErrorMessage;
        }
        finally
        {
            _isPasswordLoginRunning = false;
            PrimaryButtonText = "登录";
            IsPrimaryButtonEnabled = true;
        }
    }

    private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender?.As<Pivot>()).SelectedIndex == 1)
            StartQrLoginPolling();
        else
        {
            StopQrLoginPolling();
            InfoBarLoginHint.Title = "登录代表你同意相关条款";
        }
    }

    private void RefreshQr_Tapped(object sender, TappedRoutedEventArgs? e)
    {
        StartQrLoginPolling();
    }

    private void StartQrLoginPolling()
    {
        StopQrLoginPolling();
        InfoBarLoginHint.Severity = InfoBarSeverity.Informational;
        InfoBarLoginHint.Title = "正在加载二维码";
        InfoBarLoginHint.IsOpen = true;
        var cts = new CancellationTokenSource();
        _qrLoginCts = cts;
        var statusSessionId = Guid.NewGuid();
        _qrLoginStatusSessionId = statusSessionId;
        _ = RunQrLoginPollingAsync(cts, statusSessionId);
    }

    private async Task RunQrLoginPollingAsync(CancellationTokenSource cts, Guid statusSessionId)
    {
        try
        {
            await _loginService.StartQrLoginAsync(statusSessionId,
            async key =>
            {
                var img = await ShellLoginService.GenerateQrImageAsync(key);
                if (cts.IsCancellationRequested) return;
                QrContainer.Source = img;
            }, cts.Token);
        }
        finally
        {
            if (ReferenceEquals(_qrLoginCts, cts))
            {
                _qrLoginCts = null;
                _qrLoginStatusSessionId = null;
            }

            cts.Dispose();
        }
    }

    private void StopQrLoginPolling()
    {
        _qrLoginCts?.Cancel();
        _qrLoginCts = null;
        _qrLoginStatusSessionId = null;
    }

    private void UpdateQrLoginStatus(QrLoginStatusMessage message)
    {
        if (_qrLoginCts is null || message.SessionId != _qrLoginStatusSessionId) return;

        InfoBarLoginHint.IsOpen = true;
        InfoBarLoginHint.Severity = message.Severity;
        InfoBarLoginHint.Title = message.Title;
        if (message.Severity == InfoBarSeverity.Success)
            PrimaryButtonText = "登录成功";
    }

    private void ThirdPartyLogin_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender?.As<Button>()).Tag?.ToString();
        if (tag is null) return;
        _loginService.NavigateToThirdPartyLogin(tag);
    }
}

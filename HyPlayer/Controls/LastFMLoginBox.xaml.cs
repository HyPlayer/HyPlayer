using HyPlayer.Classes;
using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.Controls
{
    public sealed partial class LastFMLoginPage : ContentDialog
    {
        public LastFMLoginPage()
        {
            InitializeComponent();
            LastFMManager.OnLoginDone += OnLoginDone;
            LastFMManager.OnLoginError += OnLoginError;
        }
        private void OnLoginError(Exception ex)
        {
            InfoBarLoginHint.IsOpen = true;
            InfoBarLoginHint.Message = ex.Message;
            return;
        }
        private void OnLoginDone()
        {
            LastFMManager.OnLoginError -= OnLoginError;
            LastFMManager.OnLoginDone -= OnLoginDone;
            return;
        }
        private void TextBoxAccount_OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter) TextBoxPassword.Focus(FocusState.Keyboard);
        }
        private void TextBoxPassword_OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter) ButtonLogin_OnClick(null, null);
        }
        private async void ButtonLogin_OnClick(object sender, RoutedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(TextBoxAccount.Text) || string.IsNullOrWhiteSpace(TextBoxPassword.Password))
            {
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Message = "用户名或密码不能为空";
                return;
            }
            else await LastFMManager.TryLoginLastfmAccountFromInternet(TextBoxAccount.Text, TextBoxPassword.Password);
        }
        private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as Pivot).SelectedIndex == 1) _ = Launcher.LaunchUriAsync(new Uri($"https://www.last.fm/api/auth?api_key={LastFMManager.LastFMAPIKey}&cb=hyplayer://link.last.fm"));
        }

        private void ButtonCopyLink_Click(object sender, RoutedEventArgs e)
        {
            DataPackage package = new DataPackage();
            package.SetWebLink(new Uri($"https://www.last.fm/api/auth?api_key={LastFMManager.LastFMAPIKey}&cb=hyplayer://link.last.fm"));
            package.RequestedOperation = DataPackageOperation.Copy;
            Clipboard.SetContent(package);
            InfoBarLoginHint.IsOpen = true;
            InfoBarLoginHint.Message = "已复制";
        }

        private void ButtonRelaunchBrowser_Click(object sender, RoutedEventArgs e)
        {
            _ = Launcher.LaunchUriAsync(new Uri($"https://www.last.fm/api/auth?api_key={LastFMManager.LastFMAPIKey}&cb=hyplayer://link.last.fm"));
        }
    }
}

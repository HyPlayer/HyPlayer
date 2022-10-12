using HyPlayer.Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.Controls
{
    public sealed partial class LastFMLoginPage : ContentDialog
    {
        public LastFMLoginPage()
        {
            this.InitializeComponent();
            LastFMManager.OnLoginDone += OnLoginDone;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs args)
        {
            try
            {
                await LastFMManager.TryLoginLastfmAccountFromInternet(LastFMUserName.Text, LastFMPassword.Password);
            }
            catch(Exception ex)
            {
                Common.AddToTeachingTipLists("登录LastFM时发生错误",ex.Message);
            }
        }
        private void OnLoginDone()
        {
            Hide();
        }

        private void CopyLink_Click(object sender, RoutedEventArgs e)
        {
            DataPackage dataPackage = new DataPackage();
            dataPackage.RequestedOperation = DataPackageOperation.Copy;
            dataPackage.SetText("http://www.last.fm/api/auth/?api_key=" + LastFMManager.LastFMAPIKey + "&cb=hyplayer://link.last.fm");
            Clipboard.SetContent(dataPackage);
        }

        private void ReLaunchLauncher_Click(object sender, RoutedEventArgs e)
        {
            _ = Launcher.LaunchUriAsync(new Uri("http://www.last.fm/api/auth/?api_key=" + LastFMManager.LastFMAPIKey + "&cb=hyplayer://link.last.fm"));
        }

        private void PivotEx_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainPivot.SelectedIndex == 1) _ = Launcher.LaunchUriAsync(new Uri("http://www.last.fm/api/auth/?api_key=" + LastFMManager.LastFMAPIKey + "&cb=hyplayer://link.last.fm"));
        }
    }
}

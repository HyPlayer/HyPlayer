using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleComment : UserControl
    {
        private Comment comment;
        private Uri AvatarUri;
        private BitmapImage AvatarSource;
        public SingleComment(Comment cmt)
        {
            this.InitializeComponent();
            comment = cmt;
            AvatarUri = comment.AvatarUri;
            AvatarSource = new BitmapImage();
            AvatarSource.UriSource = AvatarUri;
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            DataPackage dataPackage = new DataPackage();
            dataPackage.SetText(comment.content);
            Clipboard.Clear();
            Clipboard.SetContent(dataPackage);
        }

        private async void Like_Click(object sender, RoutedEventArgs e)
        {
            await Common.ncapi.RequestAsync(CloudMusicApiProviders.CommentLike, new Dictionary<string, object>() { { "id", comment.song.sid }, { "cid", comment.cid }, { "type", "0" },{ "t", (bool)Like.IsChecked ? "0" : "1" } });
        }
    }
}

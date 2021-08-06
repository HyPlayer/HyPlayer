using System.Collections.Generic;
using Windows.UI.Xaml.Controls;
using NeteaseCloudMusicApi;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.Controls
{
    public sealed partial class CreateSonglistDialog : ContentDialog
    {
        public CreateSonglistDialog()
        {
            InitializeComponent();
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // This request would return with a 250 error without RealIP set
            string realIpBackup = Common.ncapi.RealIP;
            Common.ncapi.RealIP = "118.88.88.88";
            Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistCreate,
                new Dictionary<string, object>
                    {{"name", SonglistTitle.Text}, {"privacy", (bool) PrivateCheckBox.IsChecked ? 10 : 0}});
            Common.ncapi.RealIP = realIpBackup; // Restore user setting
        }

        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            Hide();
        }
    }
}
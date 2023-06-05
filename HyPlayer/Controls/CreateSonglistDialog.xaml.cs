#region

using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.Controls;

public sealed partial class CreateSonglistDialog : ContentDialog
{
    public CreateSonglistDialog()
    {
        InitializeComponent();
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        string realIpBackup = Common.ncapi?.RealIP;
        // This request would return with a 250 error without RealIP set
        if (Common.ncapi != null)
        {
            Common.ncapi.RealIP = "118.88.88.88";
        }

        try
        {
            await Common.ncapi?.RequestAsync(CloudMusicApiProviders.PlaylistCreate,
                new Dictionary<string, object>
                    { { "name", SonglistTitle.Text }, { "privacy", (bool)PrivateCheckBox.IsChecked ? 10 : 0 } });
        }
        catch (Exception e)
        {
            Common.AddToTeachingTipLists("创建失败", e.Message);
            return;
        }

        Common.AddToTeachingTipLists("创建成功");
        _ = Common.PageBase.LoadSongList();
        if (Common.ncapi != null)
        {
            Common.ncapi.RealIP = realIpBackup;
        }// Restore user setting
    }

    private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hide();
    }
}
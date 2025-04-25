#region

using HyPlayer.NeteaseApi.ApiContracts;
using System;
using Windows.UI.Xaml.Controls;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;

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
        string realIpBackup = Common.NeteaseAPI.Option.XRealIP;
        // This request would return with a 250 error without RealIP set
        Common.NeteaseAPI.Option.XRealIP = "118.88.88.88";

        try
        {
            var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaylistCreateApi,
                new PlaylistCreateRequest()
                {
                    Name = SonglistTitle.Text,
                    Privacy = (bool)PrivateCheckBox.IsChecked ? 10 : 0
                });
            if (result.IsError)
            {
                Common.AddToTeachingTipLists("创建失败", result.Error.Message);
            }
        }
        catch (Exception e)
        {
            Common.AddToTeachingTipLists("创建失败", e.Message);
            return;
        }

        Common.AddToTeachingTipLists("创建成功");
        _ = Common.PageBase.LoadSongList();
        Common.NeteaseAPI.Option.XRealIP = realIpBackup;// Restore user setting
    }

    private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hide();
    }
}
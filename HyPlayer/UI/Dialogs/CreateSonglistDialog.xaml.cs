#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.Services.Abstractions;
using Windows.UI.Xaml.Controls;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.UI.Dialogs;

public sealed partial class CreateSonglistDialog : ContentDialog
{
    public CreateSonglistDialog()
    {
        InitializeComponent();
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        string realIpBackup = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.XRealIP;
        // This request would return with a 250 error without RealIP set
        Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.XRealIP = "118.88.88.88";

        var result = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().RequestAsync(NeteaseApis.PlaylistCreateApi,
                new PlaylistCreateRequest()
                {
                    Name = SonglistTitle.Text,
                    Privacy = (bool)PrivateCheckBox.IsChecked ? 10 : 0
                });
        if (result.IsError)
        {
            Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("创建失败", result.Error.Message));
        }

        Ioc.Default.GetRequiredService<ITeachingTipService>().Enqueue(new("创建成功", null));
        Ioc.Default.GetRequiredService<IPlaylistCollectionChangeNotifier>().NotifyChanged();
        Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.XRealIP = realIpBackup;// Restore user setting
    }

    private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hide();
    }
}

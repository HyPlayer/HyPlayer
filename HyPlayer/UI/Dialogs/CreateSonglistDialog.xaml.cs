#region

using System;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;

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
        try
        {
            await Ioc.Default.GetRequiredService<IContainerManagementProvidable>()
                .CreateContainerAsync(SonglistTitle.Text, PrivateCheckBox.IsChecked is true);
            Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("创建成功");
            Ioc.Default.GetRequiredService<IPlaylistCollectionChangeNotifier>().NotifyChanged();
        }
        catch (Exception ex)
        {
            Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("创建失败", ex.Message);
        }
    }

    private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hide();
    }
}
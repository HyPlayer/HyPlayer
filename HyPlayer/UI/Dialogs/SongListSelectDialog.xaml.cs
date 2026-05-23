#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.Services.Abstractions;
using Windows.UI.Xaml.Controls;

#endregion

namespace HyPlayer.UI.Dialogs;

public sealed partial class SongListSelect : ContentDialog
{
    private readonly string SongId;

    public SongListSelect(string songid)
    {
        InitializeComponent();
        SongId = songid;
        ListViewSongList.Items?.Clear();
        Ioc.Default.GetRequiredService<IAuthService>().MySongLists.ForEach(t => ListViewSongList.Items?.Add(t.Name));
    }

    private async void ListViewSongList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await Ioc.Default.GetRequiredService<global::HyPlayer.NeteaseProvider.NeteaseProvider>()
            .LikeProvidableItemAsync($"sg{SongId}", Ioc.Default.GetRequiredService<IAuthService>().MySongLists[ListViewSongList.SelectedIndex].PlaylistId);
        Hide();
    }
}

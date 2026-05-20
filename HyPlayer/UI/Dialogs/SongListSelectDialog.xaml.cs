#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
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
        await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>()?.RequestAsync(NeteaseApis.PlaylistTracksEditApi,
            new PlaylistTracksEditRequest
            {
                IsAdd = true,
                PlaylistId = Ioc.Default.GetRequiredService<IAuthService>().MySongLists[ListViewSongList.SelectedIndex].PlaylistId,
                Id = SongId,
            });
        Hide();
    }
}
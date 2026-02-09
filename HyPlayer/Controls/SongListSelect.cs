#region

using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using Windows.UI.Xaml.Controls;

#endregion

namespace HyPlayer.Controls;

public sealed partial class SongListSelect : ContentDialog
{
    private readonly string SongId;

    public SongListSelect(string songid)
    {
        InitializeComponent();
        SongId = songid;
        ListViewSongList.Items?.Clear();
        Common.MySongLists.ForEach(t => ListViewSongList.Items?.Add(t.Name));
    }

    private async void ListViewSongList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await Common.NeteaseAPI?.RequestAsync(NeteaseApis.PlaylistTracksEditApi,
            new PlaylistTracksEditRequest
            {
                IsAdd = true,
                PlaylistId = Common.MySongLists[ListViewSongList.SelectedIndex].PlaylistId,
                Id = SongId,
            });
        Hide();
    }
}
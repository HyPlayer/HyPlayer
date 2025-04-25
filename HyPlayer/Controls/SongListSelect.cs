#region

using HyPlayer.NeteaseApi.ApiContracts;
using Windows.UI.Xaml.Controls;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;

#endregion

namespace HyPlayer.Controls;

public sealed partial class SongListSelect : ContentDialog
{
    private readonly string sid;

    public SongListSelect(string songid)
    {
        InitializeComponent();
        sid = songid;
        ListViewSongList.Items?.Clear();
        Common.MySongLists.ForEach(t => ListViewSongList.Items?.Add(t.name));
    }

    private async void ListViewSongList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await Common.NeteaseAPI?.RequestAsync(NeteaseApis.PlaylistTracksEditApi,
            new PlaylistTracksEditRequest
            {
                IsAdd = true,
                PlaylistId = Common.MySongLists[ListViewSongList.SelectedIndex].plid,
                Id = sid,
            });
        Hide();
    }
}
#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

#endregion

namespace HyPlayer.UI.Dialogs;

public sealed partial class SongListSelect : ContentDialog
{
    private readonly string SongId;
    private readonly IReadOnlyList<NeteasePlaylist> _songLists;

    public SongListSelect(string songid)
    {
        InitializeComponent();
        SongId = songid;
        _songLists = Ioc.Default.GetRequiredService<IAuthService>().MySongLists;
        ListViewSongList.Items?.Clear();
        foreach (var songList in _songLists)
        {
            ListViewSongList.Items?.Add(songList.Name);
        }
    }

    private async void ListViewSongList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListViewSongList.SelectedIndex < 0 || ListViewSongList.SelectedIndex >= _songLists.Count)
            return;

        var selectedSongList = _songLists[ListViewSongList.SelectedIndex];
        await new NeteasePlaylist
        {
            ActualId = selectedSongList.ActualId,
            Name = string.Empty
        }.AddSongAsync(SongId);
        Hide();
    }
}

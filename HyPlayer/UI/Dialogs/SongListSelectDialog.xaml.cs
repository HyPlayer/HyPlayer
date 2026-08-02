#region

using System.Collections.Generic;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.State;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;

#endregion

namespace HyPlayer.UI.Dialogs;

public sealed partial class SongListSelect : ContentDialog
{
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly IProvableItemLikable _likableProvider;
    private readonly IReadOnlyList<ContainerBase> _songLists;
    private readonly string _songId;

    public SongListSelect(string songid)
    {
        InitializeComponent();
        _songId = songid;
        _songLists = Ioc.Default.GetRequiredService<IUserLibraryStateService>().OwnedPlaylists;
        _likableProvider = Ioc.Default.GetRequiredService<IProvableItemLikable>();
        _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
        ListViewSongList.Items?.Clear();
        foreach (var songList in _songLists) ListViewSongList.Items?.Add(songList.Name);
    }

    private async void ListViewSongList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListViewSongList.SelectedIndex < 0 || ListViewSongList.SelectedIndex >= _songLists.Count)
            return;

        var selectedSongList = _songLists[ListViewSongList.SelectedIndex];
        var itemId = _songId.StartsWith(_knownTypeIds.SingleSongTypeId)
            ? _songId
            : _knownTypeIds.SingleSongTypeId + _songId;
        await _likableProvider.LikeProvidableItemAsync(itemId, selectedSongList.ActualId);
        Hide();
    }
}
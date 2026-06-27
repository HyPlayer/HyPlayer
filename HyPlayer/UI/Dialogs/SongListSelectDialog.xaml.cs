#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

#endregion

namespace HyPlayer.UI.Dialogs;

public sealed partial class SongListSelect : ContentDialog
{
    private readonly string SongId;
    private readonly IReadOnlyList<ContainerBase> _songLists;
    private readonly IProvableItemLikable _likableProvider;
    private readonly IProviderKnownTypeIds _knownTypeIds;

    public SongListSelect(string songid)
    {
        InitializeComponent();
        SongId = songid;
        _songLists = Ioc.Default.GetRequiredService<IAuthService>().MySongLists;
        _likableProvider = Ioc.Default.GetRequiredService<IProvableItemLikable>();
        _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
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
        var itemId = SongId.StartsWith(_knownTypeIds.SingleSongTypeId)
            ? SongId
            : _knownTypeIds.SingleSongTypeId + SongId;
        await _likableProvider.LikeProvidableItemAsync(itemId, selectedSongList.ActualId);
        Hide();
    }
}

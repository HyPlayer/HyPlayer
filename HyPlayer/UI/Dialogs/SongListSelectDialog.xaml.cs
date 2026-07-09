#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
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
        _songLists = Ioc.Default.GetRequiredService<IUserLibraryStateService>().OwnedPlaylists;
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

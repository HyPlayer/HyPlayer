#region

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.Shell.Navigation;

#endregion

namespace HyPlayer.UI.Dialogs;

public sealed partial class SongListSelect : ContentDialog
{
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly LoadState _libraryLoad;
    private readonly IProvableItemLikable _likableProvider;
    private readonly ILoadEnvironment _loadEnvironment;
    private readonly IUserLibraryStateService _userLibrary;
    private readonly string _songId;
    private List<ContainerBase> _songLists = [];

    public SongListSelect(string songid)
    {
        InitializeComponent();
        _songId = songid;
        _userLibrary = Ioc.Default.GetRequiredService<IUserLibraryStateService>();
        _likableProvider = Ioc.Default.GetRequiredService<IProvableItemLikable>();
        _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
        _loadEnvironment = Ioc.Default.GetRequiredService<ILoadEnvironment>();
        _libraryLoad = Ioc.Default.GetRequiredService<NavigationShellViewModel>().LibraryLoad;
        Opened += (_, _) =>
        {
            _libraryLoad.PropertyChanged += LibraryLoad_PropertyChanged;
            ShowSongLists();
        };
        Closed += (_, _) => _libraryLoad.PropertyChanged -= LibraryLoad_PropertyChanged;
        ShowSongLists();
    }

    private void LibraryLoad_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoadState.Phase))
            ShowSongLists();
    }

    private void ShowSongLists()
    {
        var songLists = _userLibrary.OwnedPlaylists.ToList();
        if (!songLists.SequenceEqual(_songLists) || ListViewSongList.Items?.Count != songLists.Count)
        {
            _songLists = songLists;
            ListViewSongList.Items?.Clear();
            foreach (var songList in _songLists) ListViewSongList.Items?.Add(songList.Name);
        }

        var hasSongLists = _songLists.Count > 0;
        var showLoad = !hasSongLists && (_libraryLoad.IsLoading || _libraryLoad.IsFailed);
        ListViewSongList.Visibility = hasSongLists ? Visibility.Visible : Visibility.Collapsed;
        LibraryState.State = showLoad ? _libraryLoad : null;
        EmptyHint.Visibility = hasSongLists || showLoad ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Text = _loadEnvironment.IsLoginPending
            ? "正在登录，请稍后再试"
            : _loadEnvironment.IsLoggedIn
                ? "还没有创建歌单"
                : "登录后可收藏到歌单";
    }

    private async void ListViewSongList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListViewSongList.SelectedIndex < 0 || ListViewSongList.SelectedIndex >= _songLists.Count)
            return;

        var selectedSongList = _songLists[ListViewSongList.SelectedIndex];
        var itemId = _songId.StartsWith(_knownTypeIds.SingleSongTypeId)
            ? _songId
            : _knownTypeIds.SingleSongTypeId + _songId;
        try
        {
            await _likableProvider.LikeProvidableItemAsync(itemId, selectedSongList.ActualId);
        }
        catch (Exception exception)
        {
            Ioc.Default.GetRequiredService<INotificationService>().ShowFailure("收藏到歌单失败", exception);
            ListViewSongList.SelectedIndex = -1;
            return;
        }

        Hide();
    }
}
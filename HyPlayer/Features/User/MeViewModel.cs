using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Application.Loading;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Account.Services;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;

namespace HyPlayer.Features.User;

public partial class MeViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly IProvidableItemProvidable _itemProvider;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly UISettings _settings;
    private CancellationTokenSource _cancellation = new();
    private Task<UserPlaylists>? _playlistsTask;
    private string? _requestedUserId;
    private Task<LoadedUser?>? _userTask;

    public MeViewModel(IProvidableItemProvidable itemProvider, IProviderKnownTypeIds knownTypeIds, UISettings settings,
        IAuthService auth, ILoadEnvironment loadEnvironment)
    {
        _itemProvider = itemProvider;
        _knownTypeIds = knownTypeIds;
        _settings = settings;
        _auth = auth;
        ProfileLoad = new LoadState("个人主页", loadEnvironment) { EmptyText = "没有找到这位用户" };
        MyPlaylistLoad = new LoadState("我的歌单", loadEnvironment) { EmptyText = "还没有创建歌单" };
        LikedPlaylistLoad = new LoadState("收藏歌单", loadEnvironment) { EmptyText = "还没有收藏歌单" };
    }

    public LoadState ProfileLoad { get; }
    public LoadState MyPlaylistLoad { get; }
    public LoadState LikedPlaylistLoad { get; }

    [ObservableProperty] public partial List<SimpleListItem> LikedPlaylist { get; set; }

    [ObservableProperty] public partial List<SimpleListItem> MyPlaylist { get; set; }

    [ObservableProperty] public partial UserProfileViewData User { get; set; }

    public Task Start(string? userId)
    {
        _requestedUserId = string.IsNullOrEmpty(userId) ? null : userId;
        var requiresAccount = _requestedUserId is null;
        ProfileLoad.RequiresAccount = requiresAccount;
        MyPlaylistLoad.RequiresAccount = requiresAccount;
        LikedPlaylistLoad.RequiresAccount = requiresAccount;
        return LoadProfile();
    }

    public void Cancel()
    {
        _cancellation.Cancel();
        ProfileLoad.Cancel();
        MyPlaylistLoad.Cancel();
        LikedPlaylistLoad.Cancel();
    }

    private Task LoadProfile()
    {
        if (ProfileLoad.IsLoading)
            return Task.CompletedTask;

        _cancellation.Cancel();
        _cancellation = new CancellationTokenSource();
        _userTask = null;
        _playlistsTask = null;
        var profile = ProfileLoad.RunAsync(GetUserAsync, ApplyUser, static user => user is null, LoadProfile);
        return Task.WhenAll(profile, RunPlaylistLoads());
    }

    private Task LoadPlaylists()
    {
        if (_userTask is not { IsCompletedSuccessfully: true })
            return LoadProfile();

        if (MyPlaylistLoad.IsLoading || LikedPlaylistLoad.IsLoading)
            return Task.CompletedTask;

        _playlistsTask = null;
        return RunPlaylistLoads();
    }

    private Task RunPlaylistLoads()
    {
        var mine = MyPlaylistLoad.RunAsync(GetPlaylistsAsync, playlists => MyPlaylist = playlists.Mine,
            static playlists => playlists.Mine.Count == 0, LoadPlaylists);
        var liked = LikedPlaylistLoad.RunAsync(GetPlaylistsAsync, playlists => LikedPlaylist = playlists.Liked,
            static playlists => playlists.Liked.Count == 0, LoadPlaylists);
        return Task.WhenAll(mine, liked);
    }

    private Task<LoadedUser?> GetUserAsync(CancellationToken cancellationToken)
    {
        return (_userTask ??= LoadUserAsync(_cancellation.Token)).WaitAsync(cancellationToken);
    }

    private Task<UserPlaylists> GetPlaylistsAsync(CancellationToken cancellationToken)
    {
        return (_playlistsTask ??= LoadPlaylistsAsync(_cancellation.Token)).WaitAsync(cancellationToken);
    }

    private async Task<LoadedUser?> LoadUserAsync(CancellationToken cancellationToken)
    {
        var userId = _requestedUserId ?? _auth.CurrentUser?.ActualId;
        if (string.IsNullOrEmpty(userId))
            return null;

        var item = await SimpleCacher.GetOrCreateCacheOrThrowAsync(CacheType.UserDetail, userId,
            () => LoadRequestProbe.GuardAsync(
                () => _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.UserTypeId + userId, cancellationToken),
                static found => found is null),
            cancellationToken: cancellationToken);
        if (item is not PersonBase user)
            return null;

        return new LoadedUser(user, new UserProfileViewData
        {
            ActualId = user.ActualId,
            Name = user.Name,
            Description = user is IHasDescription descriptionProvider ? descriptionProvider.Description : null,
            AvatarUrl = _settings.NoImage ? null : await TryGetCoverLinkAsync(user)
        });
    }

    private void ApplyUser(LoadedUser? loaded)
    {
        if (loaded is not null)
            User = loaded.View;
    }

    private async Task<UserPlaylists> LoadPlaylistsAsync(CancellationToken cancellationToken)
    {
        if (await GetUserAsync(cancellationToken) is not { User: var user })
            return new UserPlaylists([], []);

        var val = await SimpleCacher.GetOrCreateCacheOrThrowAsync(CacheType.UserPlaylist, user.ActualId,
            () => LoadRequestProbe.GuardAsync(
                () => user.GetSubContainerAsync(cancellationToken),
                static containers => containers is not { Count: > 0 }),
            cancellationToken: cancellationToken);

        var subListIdx = 0;
        var likedList = new List<SimpleListItem>();
        var myList = new List<SimpleListItem>();
        var playlists = new List<ContainerBase>();
        foreach (var container in val?.OfType<ContainerBase>() ?? [])
        {
            if (container.TypeId != _knownTypeIds.PlaylistTypeId)
                continue;

            var items = await LoadPlaylistContainerItemsAsync(container, cancellationToken);
            playlists.AddRange(items.OfType<ContainerBase>());
        }

        foreach (var valuePlaylist in playlists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var creators = valuePlaylist is IHasCreators creatorsProvider
                ? await creatorsProvider.GetCreatorsAsync(cancellationToken)
                : null;
            var owner = creators?.FirstOrDefault();
            var description = valuePlaylist is IHasDescription descriptionProvider
                ? descriptionProvider.Description
                : null;
            var coverLink = _settings.NoImage ? null : await TryGetCoverLinkAsync(valuePlaylist);
            var isOwned = valuePlaylist is IHasLibraryState libraryState
                ? libraryState.IsOwnedByCurrentUser
                : owner?.ActualId == user.ActualId;
            var item = new SimpleListItem
            {
                CoverLink = coverLink,
                LineOne = owner?.Name,
                LineThree = string.Empty,
                LineTwo = description,
                Order = subListIdx++,
                Route = new AppRoute.Playlist($"{valuePlaylist.ActualId}"),
                PlayResource = new MusicResource.Playlist($"{valuePlaylist.ActualId}"),
                ShowCover = !_settings.NoImage,
                Title = valuePlaylist.Name,
                CanPlay = true
            };
            if (isOwned)
                myList.Add(item);
            else
                likedList.Add(item);
        }

        return new UserPlaylists(myList, likedList);
    }

    private static async Task<List<ProvidableItemBase>> LoadPlaylistContainerItemsAsync(ContainerBase container,
        CancellationToken cancellationToken)
    {
        return container switch
        {
            LinerContainerBase liner => await liner.GetAllItemsAsync(cancellationToken),
            IProgressiveLoadingContainer progressive => (await progressive.GetProgressiveItemsListAsync(0,
                progressive.MaxProgressiveCount, cancellationToken)).Item2,
            _ => []
        };
    }

    private static async Task<string?> TryGetCoverLinkAsync(ProvidableItemBase item)
    {
        if (item is not IHasCover coverProvider)
            return null;

        var result = await coverProvider.GetCoverAsync();
        return result is IResourceResultOf<Uri?> uriResult
            ? (await uriResult.GetResourceAsync())?.GetLeftPart(UriPartial.Path)
            : null;
    }

    public sealed class UserProfileViewData
    {
        public string? ActualId { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? AvatarUrl { get; init; }
    }

    private sealed record LoadedUser(PersonBase User, UserProfileViewData View);

    private sealed record UserPlaylists(List<SimpleListItem> Mine, List<SimpleListItem> Liked);
}